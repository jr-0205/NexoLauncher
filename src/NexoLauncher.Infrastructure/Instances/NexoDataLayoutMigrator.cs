namespace NexoLauncher.Infrastructure.Instances;

public sealed class NexoDataLayoutMigrator(string instancesRoot, string versionsRoot)
{
    public int MigrateSharedVersions()
    {
        Directory.CreateDirectory(instancesRoot);
        Directory.CreateDirectory(versionsRoot);
        var migrated = 0;
        foreach (var source in Directory.EnumerateDirectories(instancesRoot).ToArray())
        {
            var version = Path.GetFileName(source);
            if (Guid.TryParseExact(version, "N", out _)) continue;
            if (!File.Exists(Path.Combine(source, version + ".json")) || !File.Exists(Path.Combine(source, version + ".jar"))) continue;
            var destination = Path.Combine(versionsRoot, version);
            if (Directory.Exists(destination)) continue;
            Directory.Move(source, destination);
            migrated++;
        }
        return migrated;
    }

    public async Task<int> NormalizeProfilesAsync(JsonInstanceRepository repository, CancellationToken cancellationToken = default)
    {
        var profiles = await repository.ListAsync(cancellationToken);
        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await repository.SaveAsync(profile, cancellationToken);
        }
        return profiles.Count;
    }
}
