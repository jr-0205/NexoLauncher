using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using NexoLauncher.Application.Instances;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Content;
using NexoLauncher.Infrastructure.Instances;

namespace NexaLauncher.Desktop;

/// <summary>
/// Mantiene las capacidades nuevas de la migración React fuera del bridge principal.
/// Sólo consume servicios C# existentes: React nunca obtiene acceso directo al disco.
/// </summary>
internal sealed class NexaDesktopMessageRouter
{
    private const string ArtworkLayoutName = "artwork-layout.json";
    private readonly CoreWebView2 webView;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly JsonInstanceRepository instances;
    private readonly InstanceManager instanceManager;
    private readonly ModrinthContentClient catalog;
    private readonly NexoBoostService boost;
    private readonly NexoBoostVisualPackService boostVisual;
    private readonly NexoBoostPresetService boostPreset = new();
    private readonly SemaphoreSlim mutationLock = new(1, 1);
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public NexaDesktopMessageRouter(NexoPaths paths, CoreWebView2 webView)
    {
        this.webView = webView;
        instances = new JsonInstanceRepository(paths.Instances);
        instanceManager = new InstanceManager(instances);
        catalog = new ModrinthContentClient(http);
        boost = new NexoBoostService(catalog);
        boostVisual = new NexoBoostVisualPackService(catalog);
    }

    public async Task<bool> TryHandleAsync(CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        DesktopRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<DesktopRequest>(eventArgs.WebMessageAsJson, json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method)) return false;
        if (!request.Method.StartsWith("boost.", StringComparison.Ordinal) &&
            !request.Method.StartsWith("artwork.", StringComparison.Ordinal)) return false;

        try
        {
            object result = request.Method switch
            {
                "boost.status" => await BoostStatusAsync(request.Payload),
                "boost.apply" => await ApplyBoostAsync(request.Payload),
                "boost.remove" => await RemoveBoostAsync(request.Payload),
                "artwork.list" => await ArtworkListAsync(),
                "artwork.update" => await UpdateArtworkAsync(request.Payload),
                _ => throw new NotSupportedException($"El método '{request.Method}' todavía no está disponible en NEXA.")
            };
            Post(new DesktopResponse(request.Id, true, result, null));
        }
        catch (Exception exception)
        {
            Post(new DesktopResponse(request.Id, false, null, exception.Message));
        }

        return true;
    }

    private async Task<object> BoostStatusAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
        var game = instances.GetPaths(id).Game;
        var components = boost.Recommend(profile.Loader);
        return new
        {
            supported = components.Count > 0,
            applied = boost.IsApplied(game),
            visualApplied = boostVisual.IsApplied(game),
            profileId = id.ToString(),
            minecraftVersion = profile.MinecraftVersion,
            loader = profile.Loader.ToString(),
            components = components.Select(component => new
            {
                id = component.ProjectId,
                name = component.Name,
                purpose = component.Purpose
            }).ToArray()
        };
    }

    private async Task<object> ApplyBoostAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        await mutationLock.WaitAsync();
        try
        {
            var profile = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var game = instances.GetPaths(id).Game;
            var components = boost.Recommend(profile.Loader);
            if (components.Count == 0)
                throw new NotSupportedException("NEXA Boost requiere Fabric, Forge o NeoForge. Por seguridad no convierte perfiles Vanilla.");

            PostEvent("operation.progress", new { stage = "Preparando NEXA Boost", completed = 0, total = 0 });

            NexoBoostApplyResult? baseResult = null;
            var alreadyApplied = boost.IsApplied(game);
            if (!alreadyApplied)
            {
                PostEvent("operation.progress", new { stage = "Instalando optimizaciones compatibles", completed = 0, total = 0 });
                baseResult = await boost.ApplyAsync(profile, game);
            }

            PostEvent("operation.progress", new { stage = "Preparando optimización visual", completed = 0, total = 0 });
            var visual = await boostVisual.ApplyAsync(profile, game);
            PostEvent("operation.progress", new { stage = "Aplicando preset Equilibrado", completed = 0, total = 0 });
            var preset = await boostPreset.ApplyAsync(game, NexoBoostPreset.Balanced);
            PostEvent("operation.progress", new { stage = "NEXA Boost listo", completed = 1, total = 1, percentage = 100 });

            return new
            {
                applied = true,
                reapplied = alreadyApplied,
                filesInstalled = (baseResult?.FilesInstalled ?? 0) + visual.FilesInstalled,
                installedFiles = (baseResult?.InstalledFiles ?? []).Concat(visual.InstalledFiles).ToArray(),
                skippedComponents = baseResult?.SkippedComponents ?? [],
                presetChanges = preset.Changes,
                particleCoreConfigured = preset.ParticleCoreConfigured,
                note = visual.Note
            };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<object> RemoveBoostAsync(JsonElement payload)
    {
        var id = InstanceId.Parse(Read<ProfileIdRequest>(payload).Id);
        await mutationLock.WaitAsync();
        try
        {
            _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var game = instances.GetPaths(id).Game;
            if (!boost.IsApplied(game)) return new { applied = false, filesRemoved = 0, valuesRestored = 0, preserved = Array.Empty<string>() };

            PostEvent("operation.progress", new { stage = "Restaurando ajustes de NEXA Boost", completed = 0, total = 0 });
            var preset = await boostPreset.RestoreAsync(game);
            var visual = await boostVisual.RemoveAsync(game);
            var baseResult = await boost.RemoveAsync(game);
            var preserved = preset.PreservedValues.Concat(visual.PreservedFiles).Concat(baseResult.PreservedFiles).ToArray();
            PostEvent("operation.progress", new { stage = "NEXA Boost desactivado", completed = 1, total = 1, percentage = 100 });

            return new
            {
                applied = false,
                filesRemoved = visual.FilesRemoved + baseResult.FilesRemoved,
                valuesRestored = preset.ValuesRestored,
                preserved
            };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private async Task<object> ArtworkListAsync()
    {
        var profiles = await instances.ListAsync();
        return profiles.Select(profile => new
        {
            id = profile.Id.ToString(),
            artwork = ReadArtworkLayout(profile.Id)
        }).ToArray();
    }

    private async Task<object> UpdateArtworkAsync(JsonElement payload)
    {
        var request = Read<ArtworkRequest>(payload);
        var id = InstanceId.Parse(request.Id);
        await mutationLock.WaitAsync();
        try
        {
            _ = await instanceManager.GetAsync(id) ?? throw new InvalidOperationException("El perfil ya no existe.");
            var placement = new ArtworkPlacement(
                request.IconPositionX,
                request.IconPositionY,
                request.IconFit,
                request.BackgroundPositionX,
                request.BackgroundPositionY,
                request.BackgroundFit).Normalize();
            await WriteArtworkLayoutAsync(id, placement);
            return new { id = id.ToString(), artwork = placement };
        }
        finally
        {
            mutationLock.Release();
        }
    }

    private ArtworkPlacement ReadArtworkLayout(InstanceId id)
    {
        var path = ArtworkLayoutPath(id, ensureDirectory: false);
        if (!File.Exists(path)) return ArtworkPlacement.Default;
        try
        {
            return (JsonSerializer.Deserialize<ArtworkPlacement>(File.ReadAllBytes(path), json) ?? ArtworkPlacement.Default).Normalize();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return ArtworkPlacement.Default;
        }
    }

    private async Task WriteArtworkLayoutAsync(InstanceId id, ArtworkPlacement placement)
    {
        var path = ArtworkLayoutPath(id, ensureDirectory: true);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(placement, json));
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string ArtworkLayoutPath(InstanceId id, bool ensureDirectory)
    {
        var root = instances.GetInstanceDirectory(id);
        var profile = Path.Combine(root, "profile");
        if (Directory.Exists(profile) && new DirectoryInfo(profile).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("profile/ no puede ser un enlace o junction para guardar el encuadre.");
        if (ensureDirectory) Directory.CreateDirectory(profile);
        return Path.Combine(profile, ArtworkLayoutName);
    }

    private static T Read<T>(JsonElement payload) =>
        payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? throw new InvalidDataException("La solicitud no contiene datos.")
            : payload.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
              ?? throw new InvalidDataException("La solicitud no pudo interpretarse.");

    private void Post(object value) => webView.PostWebMessageAsJson(JsonSerializer.Serialize(value, json));
    private void PostEvent(string name, object payload) => Post(new { @event = name, payload });

    private sealed record DesktopRequest(string Id, string Method, JsonElement Payload);
    private sealed record DesktopResponse(string Id, bool Ok, object? Result, string? Error);
    private sealed record ProfileIdRequest(string Id);
    private sealed record ArtworkRequest(
        string Id,
        double IconPositionX,
        double IconPositionY,
        string IconFit,
        double BackgroundPositionX,
        double BackgroundPositionY,
        string BackgroundFit);

    private sealed record ArtworkPlacement(
        double IconPositionX,
        double IconPositionY,
        string IconFit,
        double BackgroundPositionX,
        double BackgroundPositionY,
        string BackgroundFit)
    {
        public static ArtworkPlacement Default { get; } = new(50, 50, "contain", 50, 50, "cover");

        public ArtworkPlacement Normalize() => this with
        {
            IconPositionX = Math.Clamp(IconPositionX, 0, 100),
            IconPositionY = Math.Clamp(IconPositionY, 0, 100),
            IconFit = NormalizeFit(IconFit, "contain"),
            BackgroundPositionX = Math.Clamp(BackgroundPositionX, 0, 100),
            BackgroundPositionY = Math.Clamp(BackgroundPositionY, 0, 100),
            BackgroundFit = NormalizeFit(BackgroundFit, "cover")
        };

        private static string NormalizeFit(string? value, string fallback) =>
            string.Equals(value, "cover", StringComparison.OrdinalIgnoreCase) ? "cover" :
            string.Equals(value, "contain", StringComparison.OrdinalIgnoreCase) ? "contain" : fallback;
    }
}
