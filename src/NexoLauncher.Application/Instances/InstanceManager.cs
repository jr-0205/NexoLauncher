using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Application.Instances;

public interface IInstanceRepository
{
    Task<IReadOnlyList<GameInstance>> ListAsync(CancellationToken cancellationToken = default);
    Task<GameInstance?> GetAsync(InstanceId id, CancellationToken cancellationToken = default);
    Task SaveAsync(GameInstance instance, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(InstanceId id, CancellationToken cancellationToken = default);
    string GetInstanceDirectory(InstanceId id);
}

public sealed class InstanceManager(IInstanceRepository repository)
{
    public Task<IReadOnlyList<GameInstance>> ListAsync(CancellationToken cancellationToken = default) => repository.ListAsync(cancellationToken);

    public Task<GameInstance?> GetAsync(InstanceId id, CancellationToken cancellationToken = default) => repository.GetAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(InstanceId id, CancellationToken cancellationToken = default)
        => repository.DeleteAsync(id, cancellationToken);

    public async Task<GameInstance> CreateAsync(string name, string minecraftVersion, LoaderType loader = LoaderType.Vanilla, string? loaderVersion = null, CancellationToken cancellationToken = default)
    {
        var instance = GameInstance.Create(name, minecraftVersion, loader, loaderVersion);
        await repository.SaveAsync(instance, cancellationToken);
        return instance;
    }

    public async Task<GameInstance> CopyAsync(InstanceId sourceId, string newName, CancellationToken cancellationToken = default)
    {
        var source = await repository.GetAsync(sourceId, cancellationToken)
            ?? throw new InvalidOperationException("La instancia que deseas copiar ya no existe.");
        newName = string.IsNullOrWhiteSpace(newName) ? source.Name + " - copia" : newName.Trim();
        if (newName.Length is < 1 or > 64) throw new ArgumentException("El nombre debe tener entre 1 y 64 caracteres.", nameof(newName));

        var id = InstanceId.New();
        var now = DateTimeOffset.UtcNow;
        var copy = source with
        {
            Id = id,
            Name = newName,
            DirectoryName = id.ToString(),
            CreatedAt = now,
            UpdatedAt = now,
            LastPlayedAt = null
        };

        await repository.SaveAsync(copy, cancellationToken);
        try
        {
            var sourceGame = Path.Combine(repository.GetInstanceDirectory(sourceId), "game");
            var targetGame = Path.Combine(repository.GetInstanceDirectory(copy.Id), "game");
            if (Directory.Exists(sourceGame)) await CopyDirectoryAsync(sourceGame, targetGame, cancellationToken);
            return copy;
        }
        catch
        {
            try { await repository.DeleteAsync(copy.Id, CancellationToken.None); }
            catch { }
            throw;
        }
    }

    public async Task<GameInstance> UpdateSettingsAsync(InstanceId id, InstanceSettings settings, CancellationToken cancellationToken = default)
        => await UpdateAsync(id, null, settings, cancellationToken);

    public async Task<GameInstance> UpdateAsync(InstanceId id, string? name, InstanceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var instance = await repository.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("La instancia seleccionada ya no existe.");

        name = string.IsNullOrWhiteSpace(name) ? instance.Name : name.Trim();
        if (name.Length is < 1 or > 64) throw new ArgumentException("El nombre debe tener entre 1 y 64 caracteres.", nameof(name));

        var updated = instance with
        {
            Name = name,
            Settings = settings,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<GameInstance> MarkPlayedAsync(InstanceId id, CancellationToken cancellationToken = default)
    {
        var instance = await repository.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("La instancia seleccionada ya no existe.");
        var now = DateTimeOffset.UtcNow;
        var updated = instance with { LastPlayedAt = now, UpdatedAt = now };
        await repository.SaveAsync(updated, cancellationToken);
        return updated;
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        var sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("No se puede copiar una instancia que contiene directorios enlazados.");
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(sourceRoot, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if (new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("No se puede copiar una instancia que contiene archivos enlazados.");
            var target = Path.Combine(destination, Path.GetRelativePath(sourceRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + ".nexo-copy";
            await using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, token);
                await output.FlushAsync(token);
            }
            File.Move(temporary, target, true);
        }
    }
}
