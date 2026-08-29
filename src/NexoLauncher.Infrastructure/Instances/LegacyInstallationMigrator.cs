using NexoLauncher.Application.Instances;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Instances;

public sealed class LegacyInstallationMigrator(string versionsRoot, IInstanceRepository repository)
{
    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(versionsRoot)) return 0;
        var current = await repository.ListAsync(cancellationToken);
        var migrated = 0;

        foreach (var directory in Directory.EnumerateDirectories(versionsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = Path.GetFileName(directory);
            if (!File.Exists(Path.Combine(directory, version + ".json")) || !File.Exists(Path.Combine(directory, version + ".jar"))) continue;

            // Una versión compartida NO es una instancia. Solo creamos un perfil heredado si
            // el layout antiguo contiene game/, es decir, datos mutables que pertenecían al usuario.
            var legacyGame = Path.Combine(directory, "game");
            if (!Directory.Exists(legacyGame) || !Directory.EnumerateFileSystemEntries(legacyGame).Any()) continue;
            if (current.Any(instance => instance.MinecraftVersion == version && instance.Loader == LoaderType.Vanilla &&
                                        instance.Description.Contains("Migrada desde layout heredado", StringComparison.Ordinal))) continue;

            var instance = GameInstance.Create("Minecraft " + version, version) with
            {
                Description = "Migrada desde layout heredado de NEXO"
            };
            await repository.SaveAsync(instance, cancellationToken);
            var destination = Path.Combine(repository.GetInstanceDirectory(instance.Id), "game");
            await CopyDirectoryAsync(legacyGame, destination, cancellationToken);
            current = [.. current, instance];
            migrated++;
        }

        return migrated;
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using var output = new FileStream(target + ".nexo-migrate", FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
            await output.FlushAsync(token);
            output.Close();
            File.Move(target + ".nexo-migrate", target, true);
        }
    }
}
