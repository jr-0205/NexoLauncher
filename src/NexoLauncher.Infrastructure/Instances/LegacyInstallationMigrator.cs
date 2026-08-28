using NexoLauncher.Application.Instances;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Instances;

public sealed class LegacyInstallationMigrator(string instancesRoot, IInstanceRepository repository)
{
    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(instancesRoot)) return 0;
        var current = await repository.ListAsync(cancellationToken);
        var migrated = 0;
        foreach (var directory in Directory.EnumerateDirectories(instancesRoot))
        {
            var version = Path.GetFileName(directory);
            if (Guid.TryParseExact(version, "N", out _)) continue;
            if (!File.Exists(Path.Combine(directory, version + ".json")) || !File.Exists(Path.Combine(directory, version + ".jar"))) continue;
            if (current.Any(instance => instance.MinecraftVersion == version && instance.Loader == LoaderType.Vanilla)) continue;
            var instance = GameInstance.Create("Minecraft " + version, version);
            await repository.SaveAsync(instance, cancellationToken);
            current = [.. current, instance];
            migrated++;
        }
        return migrated;
    }
}
