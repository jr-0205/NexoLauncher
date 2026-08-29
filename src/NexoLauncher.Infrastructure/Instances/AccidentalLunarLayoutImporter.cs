using System.Text.Json;

namespace NexoLauncher.Infrastructure.Instances;

public sealed class AccidentalLunarLayoutImporter(string sourceRoot, string destinationRoot, string destinationInstances)
{
    public async Task<int> ImportAsync(CancellationToken cancellationToken = default)
    {
        var imported = 0;
        Directory.CreateDirectory(destinationRoot);
        Directory.CreateDirectory(destinationInstances);

        var profiles = Path.Combine(sourceRoot, "profiles");
        if (!Directory.Exists(profiles)) return imported;

        string[] sourceDirectories;
        try { sourceDirectories = Directory.EnumerateDirectories(profiles).ToArray(); }
        catch (IOException) { return imported; }
        catch (UnauthorizedAccessException) { return imported; }

        foreach (var sourceDirectory in sourceDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsNexoInstance(sourceDirectory)) continue;

            var name = Path.GetFileName(sourceDirectory);
            var destination = Path.Combine(destinationInstances, name);
            if (Directory.Exists(destination)) continue;

            var temporary = destination + ".importing";
            try
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
                await CopyDirectoryAsync(sourceDirectory, temporary, cancellationToken);
                Directory.Move(temporary, destination);
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException)
            {
                TryDeleteTemporary(temporary);
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteTemporary(temporary);
                continue;
            }
            catch
            {
                TryDeleteTemporary(temporary);
                throw;
            }
            imported++;
        }

        return imported;
    }

    private static bool IsNexoInstance(string directory)
    {
        try
        {
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
            var name = Path.GetFileName(directory);
            if (!Guid.TryParseExact(name, "N", out _)) return false;
            var manifest = Path.Combine(directory, "instance.json");
            if (!File.Exists(manifest)) return false;
            using var json = JsonDocument.Parse(File.ReadAllBytes(manifest));
            return json.RootElement.TryGetProperty("directoryName", out var value) &&
                   string.Equals(value.GetString(), name, StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) continue;
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await using var output = new FileStream(Path.Combine(destination, Path.GetFileName(file)), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await input.CopyToAsync(output, cancellationToken);
        }

        foreach (var child in Directory.EnumerateDirectories(source))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            await CopyDirectoryAsync(child, Path.Combine(destination, Path.GetFileName(child)), cancellationToken);
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
