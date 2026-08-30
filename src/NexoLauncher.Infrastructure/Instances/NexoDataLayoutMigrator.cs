using System.Security.Cryptography;

namespace NexoLauncher.Infrastructure.Instances;

public sealed class NexoDataLayoutMigrator(string instancesRoot, string sharedVersionsRoot)
{
    private readonly string instances = Path.GetFullPath(instancesRoot);
    private readonly string versions = Path.GetFullPath(sharedVersionsRoot);

    public int MigrateSharedVersions()
    {
        var dataRoot = Path.GetDirectoryName(instances)
            ?? throw new InvalidOperationException("No se pudo resolver la raíz de datos de NEXO.");
        var shared = Path.GetDirectoryName(versions)
            ?? throw new InvalidOperationException("No se pudo resolver la raíz compartida de NEXO.");

        Directory.CreateDirectory(instances);
        Directory.CreateDirectory(shared);
        Directory.CreateDirectory(versions);

        var migrated = 0;
        migrated += MergeLegacyShared(Path.Combine(dataRoot, "versions"), versions);
        migrated += MergeLegacyShared(Path.Combine(dataRoot, "libraries"), Path.Combine(shared, "libraries"));
        migrated += MergeLegacyShared(Path.Combine(dataRoot, "assets"), Path.Combine(shared, "assets"));
        migrated += MergeLegacyShared(Path.Combine(dataRoot, "runtime"), Path.Combine(shared, "runtimes"));

        // Layout muy antiguo: versiones de Minecraft almacenadas directamente bajo instances.
        foreach (var source in Directory.EnumerateDirectories(instances).ToArray())
        {
            var name = Path.GetFileName(source);
            if (name.StartsWith(".", StringComparison.Ordinal) || Guid.TryParseExact(name, "N", out _)) continue;
            if (!File.Exists(Path.Combine(source, name + ".json")) || !File.Exists(Path.Combine(source, name + ".jar"))) continue;
            migrated += MergeLegacyShared(source, Path.Combine(versions, name));
        }

        return migrated;
    }

    public async Task<int> NormalizeProfilesAsync(JsonInstanceRepository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        var profiles = await repository.ListAsync(cancellationToken);
        var migrated = 0;
        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = repository.GetInstanceDirectory(profile.Id);
            await repository.SaveAsync(profile, cancellationToken);
            var after = repository.GetInstanceDirectory(profile.Id);
            if (!PathsEqual(before, after) || !Path.GetFileName(after).Equals(profile.Id.ToString(), StringComparison.OrdinalIgnoreCase)) migrated++;
        }
        return migrated;
    }

    private static int MergeLegacyShared(string source, string destination)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (PathsEqual(source, destination) || !Directory.Exists(source)) return 0;

        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(source, destination);
            return 1;
        }

        var moved = 0;
        foreach (var directory in Directory.EnumerateDirectories(source).ToArray())
            moved += MergeLegacyShared(directory, Path.Combine(destination, Path.GetFileName(directory)));

        foreach (var file in Directory.EnumerateFiles(source).ToArray())
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(destination);
                File.Move(file, target);
                moved++;
                continue;
            }

            // Nunca sobrescribir silenciosamente un recurso compartido ya presente. Si ambos
            // archivos son idénticos, el duplicado heredado puede retirarse con seguridad.
            if (FilesEqual(file, target))
            {
                File.Delete(file);
                moved++;
            }
        }

        TryDeleteIfEmpty(source);
        return moved;
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).AsSpan().SequenceEqual(SHA256.HashData(rightStream));
    }

    private static void TryDeleteIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}