using System.Text.Json;
using System.Text.Json.Serialization;
using NexoLauncher.Application.Instances;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Instances;

public sealed class JsonInstanceRepository : IInstanceRepository
{
    private const string ManifestName = "instance.json";
    private readonly string root;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonInstanceRepository(string instancesRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instancesRoot);
        root = WithSeparator(Path.GetFullPath(instancesRoot));
    }

    public async Task<IReadOnlyList<GameInstance>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) return [];
        var instances = new List<GameInstance>();
        foreach (var manifest in Directory.EnumerateFiles(root, ManifestName, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = await ReadAsync(manifest, cancellationToken);
            if (instance is not null && ManifestMatchesDirectory(instance, manifest)) instances.Add(instance);
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

        var existing = await FindDirectoryAsync(instance.Id, cancellationToken);
        var desired = UniqueProfileDirectory(instance, existing);
        Directory.CreateDirectory(Path.GetDirectoryName(desired)!);
        if (existing is not null && !PathsEqual(existing, desired))
        {
            if (new DirectoryInfo(existing).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("No se puede mover una instancia enlazada a otra ubicación.");
            Directory.Move(existing, desired);
        }
        Directory.CreateDirectory(desired);

        var relativeDirectory = Path.GetRelativePath(root, desired);
        var persisted = instance with { DirectoryName = relativeDirectory };
        var manifest = Path.Combine(desired, ManifestName);
        var temporary = manifest + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, persisted, jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, manifest, true);
    }

    public async Task<bool> DeleteAsync(InstanceId id, CancellationToken cancellationToken = default)
    {
        var directory = await FindDirectoryAsync(id, cancellationToken);
        if (directory is null) return false;
        if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("No se puede eliminar una instancia enlazada a otra ubicación.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory, recursive: true);
        DeleteEmptyLoaderDirectory(Path.GetDirectoryName(directory)!);
        return true;
    }

    public string GetInstanceDirectory(InstanceId id)
    {
        var directory = FindDirectory(id);
        return directory ?? throw new DirectoryNotFoundException("La carpeta de la instancia ya no existe.");
    }

    private string UniqueProfileDirectory(GameInstance instance, string? existing)
    {
        var loaderDirectory = SafeChild(root, instance.Loader.ToString());
        var baseName = InstanceDirectoryName.Sanitize(instance.Name);
        var candidate = SafeChild(loaderDirectory, baseName);
        if (!Directory.Exists(candidate) || PathsEqual(candidate, existing)) return candidate;
        return SafeChild(loaderDirectory, $"{baseName} ({instance.Id.ToString()[..8]})");
    }

    private async Task<string?> FindDirectoryAsync(InstanceId id, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return null;
        foreach (var manifest in Directory.EnumerateFiles(root, ManifestName, SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = await ReadAsync(manifest, cancellationToken);
            if (instance?.Id == id && ManifestMatchesDirectory(instance, manifest)) return Path.GetDirectoryName(manifest)!;
        }
        return null;
    }

    private string? FindDirectory(InstanceId id)
    {
        if (!Directory.Exists(root)) return null;
        foreach (var manifest in Directory.EnumerateFiles(root, ManifestName, SearchOption.AllDirectories))
        {
            try
            {
                var instance = JsonSerializer.Deserialize<GameInstance>(File.ReadAllBytes(manifest), jsonOptions);
                if (instance?.Id == id && ManifestMatchesDirectory(instance, manifest)) return Path.GetDirectoryName(manifest)!;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return null;
    }

    private async Task<GameInstance?> ReadAsync(string manifest, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<GameInstance>(stream, jsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private bool ManifestMatchesDirectory(GameInstance instance, string manifest)
    {
        var directory = Path.GetDirectoryName(manifest)!;
        var relative = Path.GetRelativePath(root, directory);
        return string.Equals(relative, instance.DirectoryName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetFileName(directory), instance.DirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private string SafeChild(string parent, string name)
    {
        var candidate = Path.GetFullPath(Path.Combine(parent, name));
        if (!candidate.StartsWith(WithSeparator(parent), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ruta de instancia no válida.");
        return candidate;
    }

    private void DeleteEmptyLoaderDirectory(string directory)
    {
        if (!PathsEqual(Path.GetDirectoryName(directory), root.TrimEnd(Path.DirectorySeparatorChar))) return;
        try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string WithSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static bool PathsEqual(string? left, string? right) => left is not null && right is not null &&
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
