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
}
