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
        root = Path.GetFullPath(instancesRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public async Task<IReadOnlyList<GameInstance>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) return [];
        var instances = new List<GameInstance>();
        foreach (var manifest in Directory.EnumerateFiles(root, ManifestName, SearchOption.TopDirectoryOnly))
        {
            var instance = await ReadAsync(manifest, cancellationToken);
            if (instance is not null) instances.Add(instance);
        }
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var manifest = Path.Combine(directory, ManifestName);
            if (!File.Exists(manifest)) continue;
            var instance = await ReadAsync(manifest, cancellationToken);
            if (instance is not null) instances.Add(instance);
        }
        return instances.DistinctBy(value => value.Id).OrderByDescending(value => value.UpdatedAt).ToArray();
    }

    public async Task<GameInstance?> GetAsync(InstanceId id, CancellationToken cancellationToken = default)
    {
        var manifest = Path.Combine(GetInstanceDirectory(id), ManifestName);
        return File.Exists(manifest) ? await ReadAsync(manifest, cancellationToken) : null;
    }

    public async Task SaveAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!string.Equals(instance.DirectoryName, instance.Id.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException("El directorio de la instancia no coincide con su identificador.");
        var directory = GetInstanceDirectory(instance.Id);
        Directory.CreateDirectory(directory);
        var manifest = Path.Combine(directory, ManifestName);
        var temporary = manifest + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, instance, jsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, manifest, true);
    }

    public async Task<bool> DeleteAsync(InstanceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = GetInstanceDirectory(id);
        if (!Directory.Exists(directory)) return false;

        var directoryInfo = new DirectoryInfo(directory);
        if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("No se puede eliminar una instancia enlazada a otra ubicación.");

        var manifest = Path.Combine(directory, ManifestName);
        if (!File.Exists(manifest))
            throw new InvalidDataException("La carpeta seleccionada no contiene un manifiesto de instancia válido.");

        var instance = await ReadAsync(manifest, cancellationToken);
        if (instance is null || instance.Id != id ||
            !string.Equals(instance.DirectoryName, id.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException("El manifiesto no coincide con la instancia seleccionada.");

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory, recursive: true);
        return true;
    }

    public string GetInstanceDirectory(InstanceId id)
    {
        var candidate = Path.GetFullPath(Path.Combine(root, id.ToString()));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta de instancia no válida.");
        return candidate;
    }

    private async Task<GameInstance?> ReadAsync(string manifest, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<GameInstance>(stream, jsonOptions, cancellationToken);
    }
}
