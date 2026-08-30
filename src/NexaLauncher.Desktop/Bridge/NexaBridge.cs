using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Application.Instances;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Configuration;
using NexoLauncher.Infrastructure.Content;
using NexoLauncher.Infrastructure.Instances;
using NexoLauncher.Java;
using NexoLauncher.Java.Detection;
using NexoLauncher.Java.Selection;
using NexoLauncher.Minecraft;

namespace NexaLauncher.Desktop;

internal sealed class NexaBridge
{
    private const long MaxArtworkBytes = 8 * 1024 * 1024;
    private const int MaxDescriptionLength = 800;

    private readonly NexoPaths paths;
    private readonly CoreWebView2 webView;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly JsonInstanceRepository instances;
    private readonly InstanceManager instanceManager;
    private readonly JsonLauncherSettingsStore settings;
    private readonly MinecraftRuntime minecraft;
    private readonly ModrinthContentClient catalog;
    private readonly InstalledContentService installedContent = new();
    private readonly InstanceContentManager contentManager = new();
    private readonly JavaRuntimeInspector javaInspector = new();
    private readonly JavaRuntimeDetector javaDetector;
    private readonly SemaphoreSlim mutationLock = new(1, 1);
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);

    private MinecraftLaunchSession? activeLaunch;
    private InstanceId? activeLaunchProfileId;

    public NexaBridge(NexoPaths paths, CoreWebView2 webView)
    {
        this.paths = paths;
        this.webView = webView;
        instances = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instances);
        settings = new JsonLauncherSettingsStore(Path.Combine(paths.Root, "settings.json"));
        minecraft = new MinecraftRuntime(http, paths.Root, paths.Cache, paths.Logs);
        catalog = new ModrinthContentClient(http);
        javaDetector = new JavaRuntimeDetector(javaInspector);
    }

    public async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(eventArgs.WebMessageAsJson, jsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.Method))
                throw new InvalidDataException("Solicitud IPC inválida.");

            object? result;
            switch (request.Method)
            {
                case "app.bootstrap": result = await BootstrapAsync(); break;
                case "profiles.list": result = await ProfilesAsync(); break;
                case "catalog.minecraftVersions": result = await MinecraftVersionsAsync(); break;
                case "catalog.loaderVersions": result = await LoaderVersionsAsync(request.Payload); break;
                case "profiles.create": result = await CreateProfileAsync(request.Payload); break;
                case "profiles.update": result = await UpdateProfileAsync(request.Payload); break;
                case "profiles.delete": result = await DeleteProfileAsync(request.Payload); break;
                case "profiles.openFolder": result = await OpenProfileFolderAsync(request.Payload); break;
                case "profiles.launch": result = await LaunchProfileAsync(request.Payload); break;
                case "profiles.stop": result = await StopLaunchAsync(); break;
                case "content.list": result = await ContentListAsync(request.Payload); break;
                case "content.toggle": result = await ToggleContentAsync(request.Payload); break;
                case "content.delete": result = await DeleteContentAsync(request.Payload); break;
                case "content.open": result = await OpenContentAsync(request.Payload); break;
                case "content.search": result = await SearchContentAsync(request.Payload); break;
                case "content.install": result = await InstallContentAsync(request.Payload); break;
                case "settings.update": result = await UpdateSettingsAsync(request.Payload); break;
                default: throw new NotSupportedException($"El método '{request.Method}' todavía no está expuesto por NEXA Desktop Bridge.");
            }

            Post(new BridgeResponse(request.Id, true, result, null));
        }
        catch (Exception exception)
        {
            Post(new BridgeResponse(request?.Id ?? string.Empty, false, null, exception.Message));
        }
    }

    private async Task<object> BootstrapAsync()
    {
        var launcherSettings = await settings.LoadAsync();
        return new
        {
            productName = "NEXA Client",
            version = ProductVersion(),
            username = launcherSettings.Username,
            closeLauncherOnGameStart = launcherSettings.CloseLauncherOnGameStart,
            profiles = await ProfilesAsync(),
            activeLaunch = ActiveLaunchState()
        };
    }

    private async Task<IReadOnlyList<object>> ProfilesAsync()
    {
        var profiles = await instances.ListAsync();
        var result = new List<object>(profiles.Count);
        foreach (var profile in profiles) result.Add(ProfileDto(profile));
        return result;
    }

    private async Task<IReadOnlyList<object>> MinecraftVersionsAsync()
    {
        var versions = await minecraft.GetReleaseVersionsAsync();
        return versions.Select(version => (object)new
        {
            id = version.Id,
            releaseTime = version.ReleaseTime,
            stable = true
        }).ToArray();
    }

    private async Task<IReadOnlyList<object>> LoaderVersionsAsync(JsonElement payload)
    {
        var request = Read<LoaderVersionsRequest>(payload);
        var loader = ParseLoader(request.Loader);
        if (loader == LoaderType.Vanilla) return [];
        var versions = await minecraft.GetLoaderVersionsAsync(LoaderId(loader), Require(request.MinecraftVersion, "La versión de Minecraft es obligatoria."));
        return versions.Select(version => (object)new { version = version.Version, stable = version.Stable }).ToArray();
    }

    private async Task<object> CreateProfileAsync(JsonElement payload)
    {
        var request = Read<CreateProfileRequest>(payload);
        await mutationLock.WaitAsync();
        GameInstance? created = null;
        try
        {
            var name = NormalizeName(request.Name);
            var description = NormalizeDescription(request.Description);
            var loader = ParseLoader(request.Loader);
            var version = await ResolveMinecraftVersionAsync(request.MinecraftVersion);
            var loaderVersion = await ResolveLoaderVersionAsync(loader, version.Id, request.LoaderVersion);

            PostEvent("operation.progress", new { stage = "Preparando perfil", completed = 0, total = 0 });
            await EnsureInstalledAsync(version, loader, loaderVersion);

            created = await instanceManager.CreateAsync(name, version.Id, loader, loaderVersion);
            var root = instances.GetInstanceDirectory(created.Id);
            contentManager.EnsureLayout(Path.Combine(root, "game"));

            var icon = string.IsNullOrWhiteSpace(request.IconDataUrl) ? null : SaveArtwork(root, "icon", request.IconDataUrl!);
            var background = string.IsNullOrWhiteSpace(request.BackgroundDataUrl) ? null : SaveArtwork(root, "background", request.BackgroundDataUrl!);
            var memory = request.MemoryMiB is null ? created.Settings.MemoryMiB : Math.Clamp(request.MemoryMiB.Value, 1024, 32768);
            var updated = created with
            {
                Description = description,
                IconPath = icon,
                BackgroundPath = background,
                Settings = created.Settings with { MemoryMiB = memory },
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await instances.SaveAsync(updated);
            PostEvent("operation.progress", new { stage = "Perfil listo", completed = 1, total = 1 });
            return ProfileDto(updated);
        }
        catch
        {
            if (created is not null)
            {
                try { await instanceManager.DeleteAsync(created.Id); }
                catch { }
            }
            throw;
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<object> UpdateProfileAsync(JsonElement payload)
    {
        var request = Read<UpdateProfileRequest>(payload);
        await mutationLock.WaitAsync();
        try
        {
            var id = InstanceId.Parse(request.Id);
            var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var root = instances.GetInstanceDirectory(id);
            var iconPath = profile.IconPath;
            var backgroundPath = profile.BackgroundPath;

            if (request.RemoveIcon)
            {
                DeleteArtwork(root, iconPath);
                iconPath = null;
            }
            if (!string.IsNullOrWhiteSpace(request.IconDataUrl))
            {
                DeleteArtwork(root, iconPath);
                iconPath = SaveArtwork(root, "icon", request.IconDataUrl!);
            }
            if (request.RemoveBackground)
            {
                DeleteArtwork(root, backgroundPath);
                backgroundPath = null;
            }
            if (!string.IsNullOrWhiteSpace(request.BackgroundDataUrl))
            {
                DeleteArtwork(root, backgroundPath);
                backgroundPath = SaveArtwork(root, "background", request.BackgroundDataUrl!);
            }

            var updated = profile with
            {
                Name = NormalizeName(request.Name),
                Description = NormalizeDescription(request.Description),
                IconPath = iconPath,
                BackgroundPath = backgroundPath,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await instances.SaveAsync(updated);
            return ProfileDto(updated);
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<object> DeleteProfileAsync(JsonElement payload)
    {
        var request = Read<ProfileIdRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        if (activeLaunchProfileId == id && activeLaunch is not null && !activeLaunch.Process.HasExited)
            throw new InvalidOperationException("No se puede eliminar un perfil mientras Minecraft está en ejecución.");
        await mutationLock.WaitAsync();
        try { return new { deleted = await instanceManager.DeleteAsync(id) }; }
        finally { mutationLock.Release(); }
    }

    private async Task<object> OpenProfileFolderAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var directory = instances.GetInstanceDirectory(id);
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true, ArgumentList = { directory } });
        return new { opened = true };
    }

    private async Task<object> LaunchProfileAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        if (activeLaunch is not null && !activeLaunch.Process.HasExited)
            throw new InvalidOperationException("Minecraft ya está en ejecución desde NEXA.");

        var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var version = await ResolveMinecraftVersionAsync(profile.MinecraftVersion);
        await EnsureInstalledAsync(version, profile.Loader, profile.LoaderVersion);

        var requiredJava = await minecraft.GetRequiredJavaMajorAsync(version);
        var javaExecutable = await ResolveJavaExecutableAsync(profile, requiredJava);
        var launcherSettings = await settings.LoadAsync();
        var gameDirectory = instances.GetPaths(profile.Id).Game;
        contentManager.EnsureLayout(gameDirectory);
        var plan = minecraft.CreateLaunchPlan(profile.MinecraftVersion, LoaderId(profile.Loader), profile.LoaderVersion, gameDirectory);
        var options = new LaunchOptions(
            plan.VersionId,
            javaExecutable,
            launcherSettings.Username,
            profile.Settings.MemoryMiB ?? launcherSettings.MemoryMiB,
            JvmArguments: profile.Settings.JvmArguments,
            WindowWidth: profile.Settings.WindowWidth,
            WindowHeight: profile.Settings.WindowHeight,
            Fullscreen: profile.Settings.Fullscreen ?? false);

        PostEvent("operation.progress", new { stage = "Iniciando Minecraft", completed = 0, total = 0 });
        activeLaunch = minecraft.Launch(options, plan);
        activeLaunchProfileId = profile.Id;
        var refreshed = await instanceManager.MarkPlayedAsync(profile.Id);
        var session = activeLaunch;
        _ = ObserveLaunchAsync(session, profile.Id);
        PostEvent("launch.started", new { profileId = profile.Id.ToString(), pid = session.Process.Id, logPath = session.LogPath });
        return new { pid = session.Process.Id, logPath = session.LogPath, profile = ProfileDto(refreshed) };
    }

    private Task<object> StopLaunchAsync()
    {
        if (activeLaunch is null || activeLaunch.Process.HasExited) return Task.FromResult<object>(new { stopped = false });
        activeLaunch.Process.Kill(entireProcessTree: true);
        return Task.FromResult<object>(new { stopped = true });
    }

    private async Task ObserveLaunchAsync(MinecraftLaunchSession session, InstanceId profileId)
    {
        try
        {
            await session.Process.WaitForExitAsync();
            await session.OutputCompletion;
            var exitCode = session.Process.ExitCode;
            PostEvent("launch.exited", new { profileId = profileId.ToString(), exitCode, logPath = session.LogPath });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            PostEvent("launch.exited", new { profileId = profileId.ToString(), exitCode = -1, error = exception.Message, logPath = session.LogPath });
        }
        finally
        {
            if (ReferenceEquals(activeLaunch, session))
            {
                activeLaunch = null;
                activeLaunchProfileId = null;
            }
        }
    }

    private async Task<object> ContentListAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        contentManager.EnsureLayout(game);
        return installedContent.List(game);
    }

    private async Task<object> ToggleContentAsync(JsonElement payload)
    {
        var request = Read<ContentEntryRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        return installedContent.Toggle(game, request.Entry);
    }

    private async Task<object> DeleteContentAsync(JsonElement payload)
    {
        var request = Read<ContentEntryRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        installedContent.Delete(game, request.Entry);
        return new { deleted = true };
    }

    private async Task<object> OpenContentAsync(JsonElement payload)
    {
        var request = Read<ContentEntryRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        var path = installedContent.ResolvePath(game, request.Entry);
        var startInfo = new ProcessStartInfo { FileName = "explorer.exe", UseShellExecute = true };
        if (request.Entry.IsDirectory) startInfo.ArgumentList.Add(path);
        else
        {
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(path);
        }
        Process.Start(startInfo);
        return new { opened = true };
    }

    private async Task<object> SearchContentAsync(JsonElement payload)
    {
        var request = Read<ContentSearchRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var projectType = NormalizeProjectType(request.ProjectType);
        return await catalog.SearchAsync(request.Query ?? string.Empty, profile.MinecraftVersion, LoaderId(profile.Loader), projectType);
    }

    private async Task<object> InstallContentAsync(JsonElement payload)
    {
        var request = Read<ContentInstallRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        contentManager.EnsureLayout(game);
        var projectType = NormalizeProjectType(request.Project.ProjectType);
        var project = new ContentCatalogProject(
            Require(request.Project.Id, "El proyecto no tiene identificador."),
            Require(request.Project.Title, "El proyecto no tiene título."),
            request.Project.Description ?? string.Empty,
            request.Project.Author ?? string.Empty,
            projectType,
            request.Project.IconUrl,
            request.Project.Downloads);
        PostEvent("operation.progress", new { stage = $"Instalando {project.Title}", completed = 0, total = 0 });
        var result = await catalog.InstallAsync(project, profile.MinecraftVersion, LoaderId(profile.Loader), game);
        PostEvent("operation.progress", new { stage = "Contenido instalado", completed = 1, total = 1 });
        return new { result.FilesInstalled, result.FileNames, installed = installedContent.List(game) };
    }

    private async Task<object> UpdateSettingsAsync(JsonElement payload)
    {
        var request = Read<UpdateSettingsRequest>(payload);
        var current = await settings.LoadAsync();
        var username = string.IsNullOrWhiteSpace(request.Username) ? current.Username : request.Username.Trim();
        var updated = current with
        {
            Username = username,
            CloseLauncherOnGameStart = request.CloseLauncherOnGameStart ?? current.CloseLauncherOnGameStart
        };
        await settings.SaveAsync(updated);
        var normalized = updated.Normalize();
        return new { username = normalized.Username, closeLauncherOnGameStart = normalized.CloseLauncherOnGameStart };
    }

    private async Task EnsureInstalledAsync(MinecraftVersion version, LoaderType loader, string? loaderVersion)
    {
        var loaderId = LoaderId(loader);
        if (minecraft.IsInstalled(version.Id, loaderId, loaderVersion)) return;

        string? installerJava = null;
        if (loader is LoaderType.Forge or LoaderType.NeoForge)
        {
            var required = await minecraft.GetRequiredJavaMajorAsync(version);
            installerJava = (await SelectAutomaticJavaAsync(required))?.JavaExecutable
                ?? throw new InvalidOperationException($"{loader} necesita Java {required?.ToString() ?? "compatible"}, pero NEXA no encontró un runtime válido.");
        }

        var progress = new Progress<InstallProgress>(value => PostEvent("operation.progress", new
        {
            stage = value.Stage,
            completed = value.Completed,
            total = value.Total,
            percentage = value.Percentage
        }));
        await minecraft.InstallAsync(new LoaderInstallRequest(version, loaderVersion, installerJava), loaderId, progress);
    }

    private async Task<MinecraftVersion> ResolveMinecraftVersionAsync(string minecraftVersion)
    {
        var id = Require(minecraftVersion, "La versión de Minecraft es obligatoria.");
        var versions = await minecraft.GetReleaseVersionsAsync();
        return versions.FirstOrDefault(version => string.Equals(version.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Minecraft {id} ya no aparece en el catálogo oficial de versiones release.");
    }

    private async Task<string?> ResolveLoaderVersionAsync(LoaderType loader, string minecraftVersion, string? requested)
    {
        if (loader == LoaderType.Vanilla) return null;
        var available = await minecraft.GetLoaderVersionsAsync(LoaderId(loader), minecraftVersion);
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var exact = available.FirstOrDefault(value => string.Equals(value.Version, requested.Trim(), StringComparison.Ordinal));
            if (exact is null) throw new InvalidOperationException($"La versión {requested} de {loader} no es compatible con Minecraft {minecraftVersion}.");
            return exact.Version;
        }
        return available.FirstOrDefault(value => value.Stable)?.Version
               ?? available.FirstOrDefault()?.Version
               ?? throw new InvalidOperationException($"{loader} no tiene builds compatibles con Minecraft {minecraftVersion}.");
    }

    private async Task<string> ResolveJavaExecutableAsync(GameInstance profile, int? requiredMajor)
    {
        if (!string.IsNullOrWhiteSpace(profile.Settings.JavaPath))
        {
            if (!File.Exists(profile.Settings.JavaPath)) throw new FileNotFoundException("El Java configurado para este perfil ya no existe.", profile.Settings.JavaPath);
            var inspected = await javaInspector.InspectAsync(profile.Settings.JavaPath, "Profile override");
            if (inspected is null) throw new InvalidOperationException("El Java configurado para este perfil no se pudo validar.");
            if (requiredMajor is > 0 && inspected.MajorVersion != requiredMajor.Value)
                throw new InvalidOperationException($"Este perfil necesita Java {requiredMajor}, pero su override usa Java {inspected.MajorVersion}.");
            return inspected.JavaExecutable;
        }

        return (await SelectAutomaticJavaAsync(requiredMajor))?.JavaExecutable
               ?? throw new InvalidOperationException($"NEXA no encontró Java {requiredMajor?.ToString() ?? "compatible"} para este perfil.");
    }

    private async Task<JavaRuntime?> SelectAutomaticJavaAsync(int? requiredMajor)
    {
        var runtimes = await javaDetector.DetectAsync();
        return JavaRuntimeSelector.Select(runtimes, requiredMajor);
    }

    private object ProfileDto(GameInstance profile)
    {
        string? icon = null;
        string? background = null;
        try
        {
            var root = instances.GetInstanceDirectory(profile.Id);
            icon = ArtworkDataUri(root, profile.IconPath);
            background = ArtworkDataUri(root, profile.BackgroundPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _ = exception;
        }

        return new
        {
            id = profile.Id.ToString(),
            profile.Name,
            profile.Description,
            profile.MinecraftVersion,
            loader = profile.Loader.ToString(),
            profile.LoaderVersion,
            profile.LastPlayedAt,
            memoryMiB = profile.Settings.MemoryMiB,
            iconDataUrl = icon,
            backgroundDataUrl = background
        };
    }

    private object? ActiveLaunchState()
    {
        if (activeLaunch is null || activeLaunch.Process.HasExited) return null;
        return new
        {
            profileId = activeLaunchProfileId?.ToString(),
            pid = activeLaunch.Process.Id,
            logPath = activeLaunch.LogPath
        };
    }

    private string SaveArtwork(string instanceRoot, string slot, string dataUrl)
    {
        var (extension, bytes) = DecodeArtwork(dataUrl);
        var root = Path.GetFullPath(instanceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var profileDirectory = Path.Combine(root, "profile");
        Directory.CreateDirectory(profileDirectory);
        if (new DirectoryInfo(profileDirectory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("La carpeta de apariencia del perfil no puede ser un enlace o junction.");

        foreach (var previous in Directory.EnumerateFiles(profileDirectory, slot + ".*", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(previous);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("NEXA no reemplaza artwork enlazado.");
            File.Delete(previous);
        }

        var destination = Path.Combine(profileDirectory, slot + extension);
        var temporary = destination + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, destination, true);
        return Path.GetRelativePath(root, destination).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static (string Extension, byte[] Bytes) DecodeArtwork(string dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La imagen enviada no tiene un formato válido.");
        var comma = dataUrl.IndexOf(',');
        if (comma <= 0 || !dataUrl[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La imagen debe enviarse como base64.");
        var mime = dataUrl[5..comma].Split(';')[0].ToLowerInvariant();
        var extension = mime switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => throw new InvalidDataException("Formato de imagen no compatible. Usa PNG, JPG, WEBP o BMP.")
        };
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]); }
        catch (FormatException exception) { throw new InvalidDataException("La imagen base64 está dañada.", exception); }
        if (bytes.Length is <= 0 || bytes.Length > MaxArtworkBytes)
            throw new InvalidDataException("La imagen debe pesar entre 1 byte y 8 MB.");
        return (extension, bytes);
    }

    private static void DeleteArtwork(string instanceRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        var root = Path.GetFullPath(instanceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(instanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("La ruta del artwork intenta salir del perfil.");
        if (!File.Exists(candidate)) return;
        var info = new FileInfo(candidate);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("NEXA no elimina artwork enlazado.");
        File.Delete(candidate);
    }

    private static string? ArtworkDataUri(string instanceRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var root = Path.GetFullPath(instanceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(instanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) return null;
        var file = new FileInfo(candidate);
        if (file.Length <= 0 || file.Length > MaxArtworkBytes || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        var mime = Path.GetExtension(candidate).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => null
        };
        return mime is null ? null : $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(candidate))}";
    }

    private static LoaderType ParseLoader(string value)
    {
        if (!Enum.TryParse<LoaderType>(Require(value, "El loader es obligatorio."), true, out var loader) || loader == LoaderType.Quilt)
            throw new InvalidOperationException("Loader no compatible. Usa Vanilla, Fabric, Forge o NeoForge.");
        return loader;
    }

    private static string LoaderId(LoaderType loader) => loader switch
    {
        LoaderType.Vanilla => "vanilla",
        LoaderType.Fabric => "fabric",
        LoaderType.Forge => "forge",
        LoaderType.NeoForge => "neoforge",
        _ => throw new NotSupportedException($"{loader} todavía no está soportado por NEXA.")
    };

    private static string NormalizeProjectType(string value)
    {
        var normalized = Require(value, "El tipo de contenido es obligatorio.").Trim().ToLowerInvariant();
        return normalized switch
        {
            "mod" => "mod",
            "resourcepack" => "resourcepack",
            "shader" => "shader",
            "datapack" => "datapack",
            _ => throw new InvalidOperationException("Tipo de contenido no compatible.")
        };
    }

    private static string NormalizeName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 64) throw new InvalidDataException("El nombre del perfil debe tener entre 1 y 64 caracteres.");
        return name;
    }

    private static string NormalizeDescription(string? value)
    {
        var description = value?.Trim() ?? string.Empty;
        if (description.Length > MaxDescriptionLength) throw new InvalidDataException($"La descripción no puede superar {MaxDescriptionLength} caracteres.");
        return description;
    }

    private static string Require(string? value, string message)
        => string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException(message) : value.Trim();

    private T Read<T>(JsonElement payload)
        => payload.Deserialize<T>(jsonOptions) ?? throw new InvalidDataException("La solicitud no contiene los datos esperados.");

    private void Post(BridgeResponse response) => webView.PostWebMessageAsJson(JsonSerializer.Serialize(response, jsonOptions));

    private void PostEvent(string name, object payload)
        => webView.PostWebMessageAsJson(JsonSerializer.Serialize(new BridgeEvent("event", name, payload), jsonOptions));

    private static string ProductVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
        return assembly.GetName().Version?.ToString(3) ?? "0.5.2";
    }

    private sealed record BridgeRequest(string Id, string Method, JsonElement Payload);
    private sealed record BridgeResponse(string Id, bool Ok, object? Result, string? Error);
    private sealed record BridgeEvent(string Kind, string Name, object Payload);
    private sealed record ProfileIdRequest(string Id);
    private sealed record LoaderVersionsRequest(string MinecraftVersion, string Loader);
    private sealed record CreateProfileRequest(string Name, string? Description, string MinecraftVersion, string Loader, string? LoaderVersion, int? MemoryMiB, string? IconDataUrl, string? BackgroundDataUrl);
    private sealed record UpdateProfileRequest(string Id, string Name, string? Description, string? IconDataUrl, string? BackgroundDataUrl, bool RemoveIcon, bool RemoveBackground);
    private sealed record ContentEntryRequest(string Id, InstalledContentEntry Entry);
    private sealed record ContentSearchRequest(string Id, string? Query, string ProjectType);
    private sealed record ContentInstallRequest(string Id, ContentProjectRequest Project);
    private sealed record ContentProjectRequest(string Id, string Title, string? Description, string? Author, string ProjectType, string? IconUrl, long Downloads);
    private sealed record UpdateSettingsRequest(string? Username, bool? CloseLauncherOnGameStart);
}
