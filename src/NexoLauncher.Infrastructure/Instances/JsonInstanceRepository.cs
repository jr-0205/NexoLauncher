using System.Text.Json;
using System.Text.Json.Serialization;
using NexoLauncher.Application.Instances;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Instances;

public sealed class JsonInstanceRepository : IInstanceRepository
{
    public const int CurrentSchemaVersion = 2;
    private const string ManifestName = "instance.json";
    private readonly string root;
    private readonly string transientRoot;
    private readonly string migrationRoot;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonInstanceRepository(string instancesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instancesRoot);
        root = Path.GetFullPath(instancesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        transientRoot = Path.Combine(root, ".staging");
        migrationRoot = Path.Combine(root, ".migration");
        Directory.CreateDirectory(root);
        RecoverInterruptedMigrations();
        CleanupStaleStaging();
    }

    public async Task<IReadOnlyList<GameInstance>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) return [];
        var instances = new List<GameInstance>();
        foreach (var manifest in EnumerateManifests())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = await ReadAsync(manifest, cancellationToken);
            if (instance is not null) instances.Add(instance);
        }
        return instances.DistinctBy(value => value.Id).OrderByDescending(value => value.UpdatedAt).ToArray();
    }

    public async Task<GameInstance?> GetAsync(InstanceId id, CancellationToken cancellationToken = default)
    {
        var directory = await FindDirectoryAsync(id, cancellationToken);
        return directory is null ? null : await ReadAsync(Path.Combine(directory, ManifestName), cancellationToken);
    }

    public async Task SaveAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        cancellationToken.ThrowIfCancellationRequested();
        if (instance.Id.Value == Guid.Empty) throw new InvalidDataException("La instancia no tiene un GUID válido.");

        Directory.CreateDirectory(root);
        var canonical = CanonicalDirectory(instance.Id);
        var existing = await FindDirectoryAsync(instance.Id, cancellationToken);
        var persisted = instance with { DirectoryName = instance.Id.ToString() };

        if (existing is null)
        {
            await CreateTransactionalAsync(canonical, persisted, cancellationToken);
            return;
        }

        if (!PathsEqual(existing, canonical))
        {
            await MigrateLegacyDirectoryAsync(existing, canonical, persisted, cancellationToken);
            return;
        }

        EnsureSafeManagedDirectory(canonical);
        EnsureLayout(canonical);
        BackupManifestIfLegacy(canonical);
        await WriteManifestAtomicAsync(canonical, persisted, cancellationToken);
    }

    public async Task<bool> DeleteAsync(InstanceId id, CancellationToken cancellationToken = default)
    {
        var directory = await FindDirectoryAsync(id, cancellationToken);
        if (directory is null) return false;
        EnsureSafeManagedDirectory(directory);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory, recursive: true);
        DeleteEmptyParents(Path.GetDirectoryName(directory));
        return true;
    }

    public string GetInstanceDirectory(InstanceId id)
    {
        var directory = FindDirectory(id);
        return directory ?? throw new DirectoryNotFoundException("La carpeta de la instancia ya no existe.");
    }

    public InstancePaths GetPaths(InstanceId id)
    {
        var expected = CanonicalDirectory(id);
        var actual = GetInstanceDirectory(id);
        if (!PathsEqual(expected, actual))
            throw new InvalidOperationException("La instancia todavía utiliza un layout heredado y debe migrarse antes de resolver rutas privadas.");
        return new InstancePaths(root, id.Value);
    }

    private async Task CreateTransactionalAsync(string canonical, GameInstance instance, CancellationToken token)
    {
        if (Directory.Exists(canonical)) throw new IOException("Ya existe el directorio físico de esta instancia.");
        Directory.CreateDirectory(transientRoot);
        var staging = Path.Combine(transientRoot, $"{instance.Id}-{Guid.NewGuid():N}");
        EnsureChildOfRoot(staging);
        try
        {
            EnsureLayout(staging);
            await WriteManifestAtomicAsync(staging, instance, token);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, canonical);
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    private async Task MigrateLegacyDirectoryAsync(string source, string canonical, GameInstance instance, CancellationToken token)
    {
        EnsureSafeManagedDirectory(source);
        if (Directory.Exists(canonical))
            throw new IOException("No se puede migrar la instancia porque su directorio GUID ya existe.");

        Directory.CreateDirectory(migrationRoot);
        var staging = Path.Combine(migrationRoot, $"{instance.Id}-{Guid.NewGuid():N}");
        EnsureChildOfRoot(staging);
        Directory.Move(source, staging);
        try
        {
            EnsureLayout(staging);
            BackupManifest(staging, "layout-v1");
            await WriteManifestAtomicAsync(staging, instance, token);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, canonical);
            DeleteEmptyParents(Path.GetDirectoryName(source));
        }
        catch
        {
            if (Directory.Exists(staging) && !Directory.Exists(source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                Directory.Move(staging, source);
            }
            throw;
        }
    }

    private async Task<string?> FindDirectoryAsync(InstanceId id, CancellationToken cancellationToken)
    {
        var canonical = CanonicalDirectory(id);
        if (File.Exists(Path.Combine(canonical, ManifestName)))
        {
            var direct = await ReadAsync(Path.Combine(canonical, ManifestName), cancellationToken);
            if (direct?.Id == id) return canonical;
        }

        foreach (var manifest in EnumerateManifests())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = await ReadAsync(manifest, cancellationToken);
            if (instance?.Id == id) return Path.GetDirectoryName(manifest)!;
        }
        return null;
    }

    private string? FindDirectory(InstanceId id)
    {
        var canonical = CanonicalDirectory(id);
        var directManifest = Path.Combine(canonical, ManifestName);
        if (File.Exists(directManifest) && Read(directManifest)?.Id == id) return canonical;

        foreach (var manifest in EnumerateManifests())
        {
            if (Read(manifest)?.Id == id) return Path.GetDirectoryName(manifest)!;
        }
        return null;
    }

    private IEnumerable<string> EnumerateManifests()
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var manifest in Directory.EnumerateFiles(root, ManifestName, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, manifest);
            var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments[0].StartsWith('.', StringComparison.Ordinal)) continue;

            // Layout actual: <GUID>/instance.json. Layout inmediatamente anterior:
            // <Loader>/<Nombre visible>/instance.json. Nada dentro de game/, backups/ o
            // contenido importado puede convertirse accidentalmente en una instancia.
            var canonical = segments.Length == 2 &&
                            Guid.TryParseExact(segments[0], "N", out _) &&
                            string.Equals(segments[1], ManifestName, StringComparison.OrdinalIgnoreCase);
            var legacyReadable = segments.Length == 3 &&
                                 Enum.TryParse<LoaderType>(segments[0], true, out _) &&
                                 string.Equals(segments[2], ManifestName, StringComparison.OrdinalIgnoreCase);
            if (canonical || legacyReadable) yield return manifest;
        }
    }

    private async Task<GameInstance?> ReadAsync(string manifest, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(manifest, cancellationToken);
            return Deserialize(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException)
        {
            return null;
        }
    }

    private GameInstance? Read(string manifest)
    {
        try { return Deserialize(File.ReadAllBytes(manifest)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException) { return null; }
    }

    private GameInstance? Deserialize(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var jsonRoot = document.RootElement;
        if (jsonRoot.TryGetProperty("schemaVersion", out var schemaElement))
        {
            var schema = schemaElement.GetInt32();
            if (schema > CurrentSchemaVersion || schema < 1) throw new InvalidDataException($"Schema de instancia no compatible: {schema}.");
            var manifest = JsonSerializer.Deserialize<InstanceManifest>(bytes, jsonOptions)
                ?? throw new InvalidDataException("El manifiesto de instancia está vacío.");
            return manifest.ToDomain();
        }

        // Schema histórico (0.5.1 y anteriores). Se lee sin modificar el archivo; la
        // normalización de arranque lo migrará posteriormente mediante SaveAsync.
        var legacy = JsonSerializer.Deserialize<GameInstance>(bytes, jsonOptions);
        return legacy is null ? null : legacy with { DirectoryName = legacy.Id.ToString() };
    }

    private async Task WriteManifestAtomicAsync(string directory, GameInstance instance, CancellationToken token)
    {
        var manifest = Path.Combine(directory, ManifestName);
        var temporary = manifest + ".tmp";
        var dto = InstanceManifest.From(instance);
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                         FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, dto, jsonOptions, token);
            await stream.FlushAsync(token);
        }
        File.Move(temporary, manifest, true);
    }

    private void BackupManifestIfLegacy(string directory)
    {
        var manifest = Path.Combine(directory, ManifestName);
        if (!File.Exists(manifest)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifest));
            if (document.RootElement.TryGetProperty("schemaVersion", out var schema) && schema.GetInt32() == CurrentSchemaVersion) return;
        }
        catch (JsonException) { return; }
        BackupManifest(directory, "schema-v1");
    }

    private static void BackupManifest(string directory, string label)
    {
        var manifest = Path.Combine(directory, ManifestName);
        if (!File.Exists(manifest)) return;
        var backups = Path.Combine(directory, "backups");
        Directory.CreateDirectory(backups);
        var destination = Path.Combine(backups, $"instance.{label}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Copy(manifest, destination, overwrite: false);
    }

    private static void EnsureLayout(string directory)
    {
        foreach (var relative in new[]
                 {
                     "game", "game/mods", "game/config", "game/saves", "game/resourcepacks", "game/shaderpacks",
                     "game/screenshots", "game/logs", "game/crash-reports", "runtime", "runtime/natives", "backups"
                 })
            Directory.CreateDirectory(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private string CanonicalDirectory(InstanceId id)
    {
        if (id.Value == Guid.Empty) throw new InvalidDataException("GUID de instancia vacío.");
        return SafeChild(root, id.ToString());
    }

    private void EnsureSafeManagedDirectory(string directory)
    {
        var full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (PathsEqual(full, root) || !full.StartsWith(WithSeparator(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La operación intenta salir del directorio de instancias.");

        var current = new DirectoryInfo(full);
        while (current is not null && !PathsEqual(current.FullName, root))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("NEXO no opera destructivamente sobre instancias enlazadas o reparse points.");
            current = current.Parent;
        }
    }

    private void EnsureChildOfRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(WithSeparator(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ruta temporal fuera del directorio de instancias.");
    }

    private static string SafeChild(string parent, string name)
    {
        var candidate = Path.GetFullPath(Path.Combine(parent, name));
        if (!candidate.StartsWith(WithSeparator(parent), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ruta de instancia no válida.");
        return candidate;
    }

    private void RecoverInterruptedMigrations()
    {
        if (!Directory.Exists(migrationRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(migrationRoot))
        {
            var manifest = Path.Combine(directory, ManifestName);
            var instance = File.Exists(manifest) ? Read(manifest) : null;
            if (instance is null) continue;
            var canonical = CanonicalDirectory(instance.Id);
            if (Directory.Exists(canonical)) continue;
            try { Directory.Move(directory, canonical); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        TryDeleteIfEmpty(migrationRoot);
    }

    private void CleanupStaleStaging()
    {
        if (!Directory.Exists(transientRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(transientRoot))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1)))
                    Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        TryDeleteIfEmpty(transientRoot);
    }

    private void DeleteEmptyParents(string? directory)
    {
        while (!string.IsNullOrWhiteSpace(directory) &&
               !PathsEqual(directory, root) &&
               Path.GetFullPath(directory).StartsWith(WithSeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any()) break;
            try { Directory.Delete(directory); }
            catch (IOException) { break; }
            catch (UnauthorizedAccessException) { break; }
            directory = Path.GetDirectoryName(directory);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteIfEmpty(string directory)
    {
        try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string WithSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static bool PathsEqual(string? left, string? right) => left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private sealed record LoaderManifest(string Type, string? Version);
    private sealed record JavaManifest(string Mode, string? Override);
    private sealed record MemoryManifest(int MinMb, int? MaxMb);

    private sealed record InstanceManifest(
        int SchemaVersion,
        string Id,
        string Name,
        string Description,
        string? IconPath,
        string MinecraftVersion,
        LoaderManifest Loader,
        JavaManifest Java,
        MemoryManifest Memory,
        string GameDirectory,
        IReadOnlyList<string>? JvmArguments,
        int? WindowWidth,
        int? WindowHeight,
        bool? Fullscreen,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? LastPlayedAt)
    {
        public static InstanceManifest From(GameInstance instance) => new(
            CurrentSchemaVersion,
            instance.Id.ToString(),
            instance.Name,
            instance.Description,
            instance.IconPath,
            instance.MinecraftVersion,
            new LoaderManifest(instance.Loader.ToString().ToLowerInvariant(), instance.LoaderVersion),
            new JavaManifest(string.IsNullOrWhiteSpace(instance.Settings.JavaPath) ? "automatic" : "override", instance.Settings.JavaPath),
            new MemoryManifest(512, instance.Settings.MemoryMiB),
            "game",
            instance.Settings.JvmArguments,
            instance.Settings.WindowWidth,
            instance.Settings.WindowHeight,
            instance.Settings.Fullscreen,
            instance.CreatedAt,
            instance.UpdatedAt,
            instance.LastPlayedAt);

        public GameInstance ToDomain()
        {
            if (SchemaVersion is < 1 or > CurrentSchemaVersion) throw new InvalidDataException("Schema de instancia no compatible.");
            if (!string.Equals(GameDirectory, "game", StringComparison.Ordinal))
                throw new InvalidDataException("gameDirectory debe ser relativo y apuntar a 'game'.");
            var id = InstanceId.Parse(Id);
            if (!Enum.TryParse<LoaderType>(Loader.Type, ignoreCase: true, out var loader))
                throw new InvalidDataException($"Loader no reconocido: {Loader.Type}.");
            if (loader != LoaderType.Vanilla && string.IsNullOrWhiteSpace(Loader.Version))
                throw new InvalidDataException("El loader de la instancia no tiene versión.");
            var javaPath = string.Equals(Java.Mode, "override", StringComparison.OrdinalIgnoreCase) ? Java.Override : null;
            return new GameInstance
            {
                Id = id,
                Name = Name,
                Description = Description ?? string.Empty,
                IconPath = IconPath,
                MinecraftVersion = MinecraftVersion,
                Loader = loader,
                LoaderVersion = loader == LoaderType.Vanilla ? null : Loader.Version,
                DirectoryName = id.ToString(),
                Settings = new InstanceSettings(Memory.MaxMb, javaPath, JvmArguments, WindowWidth, WindowHeight, Fullscreen),
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                LastPlayedAt = LastPlayedAt
            };
        }
    }
}
