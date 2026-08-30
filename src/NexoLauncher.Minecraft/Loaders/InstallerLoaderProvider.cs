using System.Diagnostics;
using System.Text.Json;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Installation;

namespace NexoLauncher.Minecraft.Loaders;

public sealed class InstallerLoaderProvider(
    string id,
    InstallerLoaderMetadataClient metadata,
    VanillaInstaller vanilla,
    VerifiedDownloader downloader,
    MinecraftPaths paths) : ILoaderProvider
{
    public string Id { get; } = id is "forge" or "neoforge" ? id : throw new ArgumentOutOfRangeException(nameof(id));

    public Task<IReadOnlyList<LoaderVersion>> GetVersionsAsync(string minecraftVersion, CancellationToken token = default)
        => Id == "forge" ? metadata.GetForgeVersionsAsync(minecraftVersion, token) : metadata.GetNeoForgeVersionsAsync(minecraftVersion, token);

    public bool IsInstalled(string minecraftVersion, string? loaderVersion)
        => !string.IsNullOrWhiteSpace(loaderVersion) && vanilla.IsInstalled(minecraftVersion)
           && File.Exists(paths.LoaderProfile(Id, minecraftVersion, loaderVersion));

    public async Task InstallAsync(LoaderInstallRequest request, IProgress<InstallProgress>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(request.LoaderVersion)) throw new ArgumentException($"{Id} requiere una versión de loader.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.JavaExecutable) || !File.Exists(request.JavaExecutable))
            throw new FileNotFoundException($"{Id} requiere un runtime Java válido para ejecutar su instalador.", request.JavaExecutable);

        if (!vanilla.IsInstalled(request.Version.Id)) await vanilla.InstallAsync(request.Version, progress, token);
        PrepareOfficialLayout(request.Version.Id);

        var installer = paths.LoaderInstaller(Id, request.Version.Id, request.LoaderVersion);
        var url = Id == "forge"
            ? InstallerLoaderMetadataClient.ForgeInstallerUrl(request.Version.Id, request.LoaderVersion)
            : InstallerLoaderMetadataClient.NeoForgeInstallerUrl(request.LoaderVersion);
        progress?.Report(new($"Descargando instalador de {Id}", 0, 1));
        var sha1 = await metadata.GetSha1Async(url, token);
        await downloader.DownloadAsync(url, installer, sha1, token);

        progress?.Report(new($"Ejecutando instalador oficial de {Id}", 0, 1));
        var startedAt = DateTime.UtcNow;
        await RunInstallerAsync(request.JavaExecutable, installer, token);
        ImportGeneratedProfile(request.Version.Id, request.LoaderVersion, startedAt);
        progress?.Report(new($"{Id} listo", 1, 1));
    }

    public LaunchPlan CreateLaunchPlan(string minecraftVersion, string? loaderVersion, string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion)) throw new ArgumentException($"{Id} requiere una versión.", nameof(loaderVersion));
        var profilePath = paths.LoaderProfile(Id, minecraftVersion, loaderVersion);
        if (!File.Exists(profilePath)) throw new FileNotFoundException($"El perfil de {Id} no está instalado.", profilePath);
        using var profile = JsonDocument.Parse(File.ReadAllBytes(profilePath));
        var root = profile.RootElement;
        var libraries = root.GetProperty("libraries").EnumerateArray()
            .Select(LibraryPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missing = libraries.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null) throw new FileNotFoundException($"La instalación de {Id} está incompleta; falta una biblioteca compartida y debe repararse.", missing);
        return new LaunchPlan(minecraftVersion, Path.GetFullPath(gameDirectory),
            root.GetProperty("mainClass").GetString(), libraries, ReadArguments(root, "jvm"), ReadArguments(root, "game"));
    }

    private void PrepareOfficialLayout(string minecraftVersion)
    {
        // shared/ ya ES el layout oficial usado por el instalador. No copiar los archivos
        // de versión sobre sí mismos; únicamente validamos que Vanilla esté listo y creamos
        // launcher_profiles.json, que algunos instaladores oficiales esperan encontrar.
        if (!File.Exists(paths.VersionJson(minecraftVersion)) || !File.Exists(paths.ClientJar(minecraftVersion)))
            throw new InvalidDataException($"Minecraft {minecraftVersion} no está completo antes de instalar {Id}.");
        Directory.CreateDirectory(paths.VersionDirectory(minecraftVersion));
        Directory.CreateDirectory(paths.Libraries);
        Directory.CreateDirectory(paths.Assets);
        var profiles = Path.Combine(paths.Root, "launcher_profiles.json");
        if (!File.Exists(profiles)) File.WriteAllText(profiles, "{\"profiles\":{}}");
    }

    private async Task RunInstallerAsync(string java, string installer, CancellationToken token)
    {
        var info = new ProcessStartInfo(java)
        {
            WorkingDirectory = paths.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-jar");
        info.ArgumentList.Add(installer);
        info.ArgumentList.Add("--installClient");
        info.ArgumentList.Add(paths.Root);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar el instalador del loader.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException($"El instalador oficial de {Id} terminó con código {process.ExitCode}: {output.Trim()}");
    }

    private void ImportGeneratedProfile(string minecraftVersion, string loaderVersion, DateTime startedAt)
    {
        var versions = Path.Combine(paths.Root, "versions");
        var target = paths.LoaderProfile(Id, minecraftVersion, loaderVersion);
        var candidates = Directory.EnumerateFiles(versions, "*.json", SearchOption.AllDirectories)
            .Where(path => !PathsEqual(path, paths.VersionJson(minecraftVersion)))
            .Where(path => !PathsEqual(path, target))
            .Select(path => (Path: path, Time: File.GetLastWriteTimeUtc(path)))
            .Where(item => item.Time >= startedAt.AddSeconds(-2))
            .OrderByDescending(item => item.Time)
            .ToArray();
        var source = candidates.Select(item => item.Path).FirstOrDefault(path => MatchesProfile(path, minecraftVersion));
        if (source is null) throw new InvalidDataException($"El instalador de {Id} no generó un perfil compatible con Minecraft {minecraftVersion}.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }

    private static bool MatchesProfile(string path, string minecraftVersion)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            return json.RootElement.TryGetProperty("inheritsFrom", out var value) && value.GetString() == minecraftVersion;
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
    }

    private string LibraryPath(JsonElement library)
    {
        if (library.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact)
            && artifact.TryGetProperty("path", out var path))
            return SafeLibraryPath(path.GetString() ?? throw new InvalidDataException($"{Id} publicó una biblioteca sin ruta."));
        var coordinate = library.GetProperty("name").GetString() ?? throw new InvalidDataException($"{Id} publicó una biblioteca sin coordenada.");
        var relative = FabricLibraryResolver.Resolve(coordinate).RelativePath;
        return SafeLibraryPath(relative);
    }

    private string SafeLibraryPath(string relative)
    {
        var target = Path.GetFullPath(Path.Combine(paths.Libraries, relative.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(paths.Libraries).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{Id} publicó una ruta de biblioteca fuera de shared/libraries.");
        return target;
    }

    private static IReadOnlyList<string> ReadArguments(JsonElement root, string kind)
    {
        if (!root.TryGetProperty("arguments", out var arguments) || !arguments.TryGetProperty(kind, out var values)) return [];
        return values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
