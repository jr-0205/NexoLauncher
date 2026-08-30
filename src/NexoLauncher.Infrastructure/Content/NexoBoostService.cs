using System.Security.Cryptography;
using System.Text.Json;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Content;

public sealed record NexoBoostComponent(string ProjectId, string Name, string Purpose, bool Renderer = false);
public sealed record NexoBoostApplyResult(int FilesInstalled, IReadOnlyList<string> InstalledFiles, IReadOnlyList<string> SkippedComponents);
public sealed record NexoBoostRemoveResult(int FilesRemoved, IReadOnlyList<string> PreservedFiles);

/// <summary>
/// Instala un conjunto pequeño de optimizaciones cliente desde Modrinth usando la
/// versión de Minecraft y loader exactos de la instancia. El proceso es transaccional:
/// no publica archivos hasta que todas las descargas compatibles terminaron.
/// </summary>
public sealed class NexoBoostService(ModrinthContentClient catalog)
{
    private const int ManifestSchema = 1;
    private const string ManifestName = "nexo-boost.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public bool IsApplied(string gameDirectory) => File.Exists(ManifestPath(gameDirectory));

    public IReadOnlyList<NexoBoostComponent> Recommend(LoaderType loader) => loader switch
    {
        LoaderType.Fabric =>
        [
            new("sodium", "Sodium", "Motor de renderizado de alto rendimiento", true),
            new("lithium", "Lithium", "Optimiza lógica, ticks y física"),
            new("ferrite-core", "FerriteCore", "Reduce uso y presión de memoria"),
            new("immediatelyfast", "ImmediatelyFast", "Reduce coste de entidades, partículas, texto y HUD"),
            new("entityculling", "Entity Culling", "Evita renderizar entidades ocultas"),
            new("modernfix", "ModernFix", "Optimización general y reducción de memoria")
        ],
        LoaderType.NeoForge =>
        [
            new("sodium", "Sodium", "Motor de renderizado de alto rendimiento", true),
            new("lithium", "Lithium", "Optimiza lógica, ticks y física"),
            new("ferrite-core", "FerriteCore", "Reduce uso y presión de memoria"),
            new("immediatelyfast", "ImmediatelyFast", "Reduce coste de entidades, partículas, texto y HUD"),
            new("entityculling", "Entity Culling", "Evita renderizar entidades ocultas"),
            new("modernfix", "ModernFix", "Optimización general y reducción de memoria")
        ],
        LoaderType.Forge =>
        [
            new("embeddium", "Embeddium", "Renderer optimizado para el ecosistema Forge", true),
            new("ferrite-core", "FerriteCore", "Reduce uso y presión de memoria"),
            new("immediatelyfast", "ImmediatelyFast", "Reduce coste de entidades, partículas, texto y HUD"),
            new("entityculling", "Entity Culling", "Evita renderizar entidades ocultas"),
            new("modernfix", "ModernFix", "Optimización general y reducción de memoria")
        ],
        _ => []
    };

    public async Task<NexoBoostApplyResult> ApplyAsync(
        GameInstance instance,
        string gameDirectory,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        gameDirectory = Path.GetFullPath(gameDirectory);
        var components = Recommend(instance.Loader);
        if (components.Count == 0)
            throw new NotSupportedException("NEXO Boost requiere una instancia Fabric, Forge o NeoForge. No modifica perfiles Vanilla automáticamente.");
        if (IsApplied(gameDirectory))
            throw new InvalidOperationException("NEXO Boost ya está activo en esta instancia. Desactívalo antes de volver a aplicarlo.");

        Directory.CreateDirectory(gameDirectory);
        var finalMods = Path.Combine(gameDirectory, "mods");
        Directory.CreateDirectory(finalMods);
        var existingNames = Directory.EnumerateFiles(finalMods, "*.jar", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        var skipped = new List<string>();
        using var transaction = ContentImportTransaction.Begin(gameDirectory);
        foreach (var component in components)
        {
            token.ThrowIfCancellationRequested();
            if (component.Renderer && HasKnownRenderer(existingNames))
            {
                skipped.Add($"{component.Name}: ya existe un renderer de rendimiento");
                continue;
            }
            if (LooksInstalled(existingNames, component.ProjectId))
            {
                skipped.Add($"{component.Name}: ya parece estar instalado");
                continue;
            }

            try
            {
                await catalog.InstallAsync(
                    new ContentCatalogProject(component.ProjectId, component.Name, component.Purpose, "NEXO", "mod", null, 0),
                    instance.MinecraftVersion,
                    LoaderName(instance.Loader),
                    transaction.StagingGameDirectory,
                    token);
            }
            catch (InvalidOperationException exception) when (IsCompatibilityMiss(exception))
            {
                skipped.Add($"{component.Name}: sin build compatible para {instance.MinecraftVersion}/{instance.Loader}");
            }
        }

        var stagingMods = Path.Combine(transaction.StagingGameDirectory, "mods");
        if (!Directory.Exists(stagingMods))
            return new NexoBoostApplyResult(0, [], skipped);

        // Nunca reemplazar un JAR que ya pertenece al usuario. Si el mismo nombre existe
        // en la instancia real, conservamos el original y retiramos la copia de staging.
        foreach (var staged in Directory.EnumerateFiles(stagingMods, "*.jar", SearchOption.TopDirectoryOnly).ToArray())
        {
            var destination = Path.Combine(finalMods, Path.GetFileName(staged));
            if (!File.Exists(destination)) continue;
            File.Delete(staged);
            skipped.Add($"{Path.GetFileName(staged)}: archivo existente preservado");
        }

        var stagedNames = Directory.EnumerateFiles(stagingMods, "*.jar", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (stagedNames.Length == 0)
            return new NexoBoostApplyResult(0, [], skipped);

        transaction.Commit();

        var managed = new List<ManagedBoostFile>();
        foreach (var name in stagedNames)
        {
            var path = Path.Combine(finalMods, name);
            if (!File.Exists(path)) continue;
            managed.Add(new ManagedBoostFile(Path.Combine("mods", name).Replace('\\', '/'), await Sha512Async(path, token)));
        }

        await WriteManifestAsync(gameDirectory,
            new BoostManifest(ManifestSchema, instance.MinecraftVersion, LoaderName(instance.Loader), DateTimeOffset.UtcNow, managed), token);
        return new NexoBoostApplyResult(managed.Count, managed.Select(file => Path.GetFileName(file.RelativePath)).ToArray(), skipped);
    }

    public async Task<NexoBoostRemoveResult> RemoveAsync(string gameDirectory, CancellationToken token = default)
    {
        gameDirectory = Path.GetFullPath(gameDirectory);
        var manifestPath = ManifestPath(gameDirectory);
        if (!File.Exists(manifestPath)) return new NexoBoostRemoveResult(0, []);

        BoostManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<BoostManifest>(await File.ReadAllBytesAsync(manifestPath, token), Json)
                       ?? throw new InvalidDataException("El manifiesto de NEXO Boost está vacío.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("El manifiesto de NEXO Boost está dañado; NEXO no borrará mods sin poder verificar su propiedad.", exception);
        }

        var removed = 0;
        var preserved = new List<string>();
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested();
            var destination = SafeGameFile(gameDirectory, file.RelativePath);
            if (!File.Exists(destination)) continue;
            var currentHash = await Sha512Async(destination, token);
            if (!string.Equals(currentHash, file.Sha512, StringComparison.OrdinalIgnoreCase))
            {
                preserved.Add(file.RelativePath + " (modificado después de aplicar Boost)");
                continue;
            }
            File.Delete(destination);
            removed++;
        }

        File.Delete(manifestPath);
        return new NexoBoostRemoveResult(removed, preserved);
    }

    private static bool IsCompatibilityMiss(InvalidOperationException exception) =>
        exception.Message.Contains("no tiene una versión compatible", StringComparison.OrdinalIgnoreCase);

    private static bool LooksInstalled(IEnumerable<string> fileNames, string projectId)
    {
        var normalized = projectId.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return fileNames.Any(name => name.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant()
            .Contains(normalized, StringComparison.Ordinal));
    }

    private static bool HasKnownRenderer(IEnumerable<string> fileNames)
    {
        string[] aliases = ["sodium", "embeddium", "rubidium", "optifine", "vulkanmod"];
        return fileNames.Any(name => aliases.Any(alias => name.Contains(alias, StringComparison.OrdinalIgnoreCase)));
    }

    private static string LoaderName(LoaderType loader) => loader switch
    {
        LoaderType.Fabric => "fabric",
        LoaderType.Forge => "forge",
        LoaderType.NeoForge => "neoforge",
        _ => "vanilla"
    };

    private static string ManifestPath(string gameDirectory)
    {
        var game = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var instanceRoot = Directory.GetParent(game)?.FullName
                           ?? throw new InvalidOperationException("No se pudo resolver la raíz de la instancia.");
        var runtime = Path.Combine(instanceRoot, "runtime");
        Directory.CreateDirectory(runtime);
        return Path.Combine(runtime, ManifestName);
    }

    private static async Task WriteManifestAsync(string gameDirectory, BoostManifest manifest, CancellationToken token)
    {
        var path = ManifestPath(gameDirectory);
        var temporary = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        await File.WriteAllBytesAsync(temporary, bytes, token);
        using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
            stream.Flush(flushToDisk: true);
        File.Move(temporary, path, true);
    }

    private static string SafeGameFile(string gameDirectory, string relativePath)
    {
        var root = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part is "." or ".." || part.Contains(':')))
            throw new InvalidDataException("El manifiesto de Boost contiene una ruta no válida.");
        var candidate = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El manifiesto de Boost intenta salir del gameDirectory.");
        ContentImportTransaction.EnsurePhysicalDestination(root, candidate);
        return candidate;
    }

    private static async Task<string> Sha512Async(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private sealed record ManagedBoostFile(string RelativePath, string Sha512);
    private sealed record BoostManifest(int SchemaVersion, string MinecraftVersion, string Loader, DateTimeOffset AppliedAt, IReadOnlyList<ManagedBoostFile> Files);
}
