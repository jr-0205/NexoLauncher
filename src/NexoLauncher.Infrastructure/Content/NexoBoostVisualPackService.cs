using System.Security.Cryptography;
using System.Text.Json;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Content;

public sealed record NexoBoostVisualApplyResult(int FilesInstalled, IReadOnlyList<string> InstalledFiles, string? Note);
public sealed record NexoBoostVisualRemoveResult(int FilesRemoved, IReadOnlyList<string> PreservedFiles);

/// <summary>
/// Complemento visual de NEXO Boost. Instala Particle Core (y dependencias requeridas)
/// de forma transaccional para poder optimizar partículas sin recurrir al ajuste
/// global "Minimal", que elimina señales visuales útiles de combate.
/// </summary>
public sealed class NexoBoostVisualPackService(ModrinthContentClient catalog)
{
    private const int ManifestSchema = 1;
    private const string ManifestName = "nexo-boost-visual.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public bool IsApplied(string gameDirectory) => File.Exists(ManifestPath(gameDirectory, ensureRuntime: false));

    public async Task<NexoBoostVisualApplyResult> ApplyAsync(GameInstance instance, string gameDirectory, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.Loader == LoaderType.Vanilla)
            return new NexoBoostVisualApplyResult(0, [], "Particle Core requiere Fabric, Forge o NeoForge.");

        gameDirectory = Path.GetFullPath(gameDirectory);
        if (IsApplied(gameDirectory))
            return new NexoBoostVisualApplyResult(0, [], "El optimizador de partículas de NEXO Boost ya está instalado.");

        var finalMods = Path.Combine(gameDirectory, "mods");
        Directory.CreateDirectory(finalMods);
        var existingNames = Directory.EnumerateFiles(finalMods, "*.jar", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var transaction = ContentImportTransaction.Begin(gameDirectory);
        try
        {
            await catalog.InstallAsync(
                new ContentCatalogProject(
                    "particle-core",
                    "Particle Core",
                    "Optimiza y permite reducir partículas por tipo sin sacrificar las de combate",
                    "NEXO",
                    "mod",
                    null,
                    0),
                instance.MinecraftVersion,
                LoaderName(instance.Loader),
                transaction.StagingGameDirectory,
                token);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("no tiene una versión compatible", StringComparison.OrdinalIgnoreCase))
        {
            return new NexoBoostVisualApplyResult(0, [], $"Particle Core no tiene build compatible para {instance.MinecraftVersion}/{instance.Loader}.");
        }

        var stagingMods = Path.Combine(transaction.StagingGameDirectory, "mods");
        if (!Directory.Exists(stagingMods))
            return new NexoBoostVisualApplyResult(0, [], "Particle Core no produjo archivos instalables para este perfil.");

        foreach (var staged in Directory.EnumerateFiles(stagingMods, "*.jar", SearchOption.TopDirectoryOnly).ToArray())
        {
            if (!existingNames.Contains(Path.GetFileName(staged))) continue;
            File.Delete(staged);
        }

        var names = Directory.EnumerateFiles(stagingMods, "*.jar", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length == 0)
            return new NexoBoostVisualApplyResult(0, [], "Particle Core o sus dependencias ya estaban presentes y fueron preservados.");

        transaction.Commit();
        var managed = new List<ManagedVisualFile>();
        foreach (var name in names)
        {
            var path = Path.Combine(finalMods, name);
            if (!File.Exists(path)) continue;
            managed.Add(new ManagedVisualFile(Path.Combine("mods", name).Replace('\\', '/'), await Sha512Async(path, token)));
        }

        await WriteManifestAsync(gameDirectory,
            new VisualManifest(ManifestSchema, instance.MinecraftVersion, LoaderName(instance.Loader), DateTimeOffset.UtcNow, managed), token);
        return new NexoBoostVisualApplyResult(managed.Count, managed.Select(item => Path.GetFileName(item.RelativePath)!).ToArray(), null);
    }

    public async Task<NexoBoostVisualRemoveResult> RemoveAsync(string gameDirectory, CancellationToken token = default)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        var manifestPath = ManifestPath(gameDirectory, ensureRuntime: false);
        if (!File.Exists(manifestPath)) return new NexoBoostVisualRemoveResult(0, []);

        VisualManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<VisualManifest>(await File.ReadAllBytesAsync(manifestPath, token), Json)
                       ?? throw new InvalidDataException("El manifiesto visual de NEXO Boost está vacío.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("El manifiesto visual de NEXO Boost está dañado; no se eliminarán mods a ciegas.", exception);
        }

        if (manifest.SchemaVersion != ManifestSchema || manifest.Files is null)
            throw new InvalidDataException("El manifiesto visual de NEXO Boost no es compatible; no se eliminarán mods a ciegas.");

        var removed = 0;
        var preserved = new List<string>();
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            var destination = SafeGameFile(gameDirectory, file.RelativePath);
            if (!File.Exists(destination)) continue;
            var hash = await Sha512Async(destination, token);
            if (!string.Equals(hash, file.Sha512, StringComparison.OrdinalIgnoreCase))
            {
                preserved.Add(file.RelativePath + " (modificado después de instalar el optimizador visual)");
                continue;
            }
            File.Delete(destination);
            removed++;
        }

        File.Delete(manifestPath);
        return new NexoBoostVisualRemoveResult(removed, preserved);
    }

    private static string LoaderName(LoaderType loader) => loader switch
    {
        LoaderType.Fabric => "fabric",
        LoaderType.Forge => "forge",
        LoaderType.NeoForge => "neoforge",
        _ => "vanilla"
    };

    private static string ManifestPath(string gameDirectory, bool ensureRuntime)
    {
        var game = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var instanceRoot = Directory.GetParent(game)?.FullName
                           ?? throw new InvalidOperationException("No se pudo resolver la raíz de la instancia.");
        var runtime = Path.Combine(instanceRoot, "runtime");
        if (Directory.Exists(runtime) && new DirectoryInfo(runtime).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("runtime/ no puede ser un enlace o junction.");
        if (ensureRuntime) Directory.CreateDirectory(runtime);
        return Path.Combine(runtime, ManifestName);
    }

    private static async Task WriteManifestAsync(string gameDirectory, VisualManifest manifest, CancellationToken token)
    {
        var path = ManifestPath(gameDirectory, ensureRuntime: true);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(manifest, Json), token);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string SafeGameFile(string gameDirectory, string relativePath)
    {
        var root = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part is "." or ".." || part.Contains(':')))
            throw new InvalidDataException("El manifiesto visual de Boost contiene una ruta no válida.");
        var candidate = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El manifiesto visual de Boost intenta salir del gameDirectory.");
        ContentImportTransaction.EnsurePhysicalDestination(root, candidate);
        return candidate;
    }

    private static async Task<string> Sha512Async(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private sealed record ManagedVisualFile(string RelativePath, string Sha512);
    private sealed record VisualManifest(
        int SchemaVersion,
        string MinecraftVersion,
        string Loader,
        DateTimeOffset AppliedAt,
        IReadOnlyList<ManagedVisualFile>? Files);
}
