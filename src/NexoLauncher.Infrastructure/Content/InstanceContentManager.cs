using System.IO.Compression;
using System.Text.Json;

namespace NexoLauncher.Infrastructure.Content;

public sealed record ContentImportResult(int FilesInstalled, int ReferencedFilesMissing, IReadOnlyList<string> Destinations)
{
    public static ContentImportResult Empty { get; } = new(0, 0, []);
}

public sealed class InstanceContentManager
{
    private static readonly HashSet<string> OverrideRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "mods", "resourcepacks", "shaderpacks", "config", "defaultconfigs", "kubejs", "scripts",
        "saves", "screenshots", "options.txt", "servers.dat", "journeymap", "xaero", "rewind", "showdown"
    };

    public void EnsureLayout(string gameDirectory)
    {
        var root = NormalizeRoot(gameDirectory);
        foreach (var folder in new[] { "mods", "resourcepacks", "shaderpacks", "config", "defaultconfigs", "kubejs", "saves" })
            Directory.CreateDirectory(Path.Combine(root, folder));
    }

    public async Task<ContentImportResult> ImportAsync(string gameDirectory, IEnumerable<string> sourcePaths, string? minecraftVersion = null, string? loaderId = null, CancellationToken token = default)
    {
        var root = NormalizeRoot(gameDirectory);
        EnsureLayout(root);
        var installed = 0;
        var missing = 0;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var fullSource = Path.GetFullPath(source);
            if (!File.Exists(fullSource)) throw new FileNotFoundException("No se encontró el complemento seleccionado.", fullSource);

            var extension = Path.GetExtension(fullSource);
            if (extension.Equals(".jar", StringComparison.OrdinalIgnoreCase))
            {
                await CopyAtomicAsync(fullSource, Path.Combine(root, "mods", Path.GetFileName(fullSource)), token);
                installed++;
                destinations.Add("mods");
                continue;
            }

            if (extension.Equals(".lcpack", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                var result = await ImportPackArchiveAsync(root, fullSource, minecraftVersion, loaderId, token);
                installed += result.FilesInstalled;
                missing += result.ReferencedFilesMissing;
                foreach (var destination in result.Destinations) destinations.Add(destination);
                continue;
            }

            if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"El formato '{extension}' no es compatible como complemento.");

            using var archive = ZipFile.OpenRead(fullSource);
            var names = archive.Entries.Select(entry => NormalizeEntry(entry.FullName)).ToArray();
            if (names.Any(IsOverrideEntry))
            {
                installed += await ExtractEntriesAsync(archive, root, entry => IsOverrideEntry(NormalizeEntry(entry.FullName)), stripOverrides: false, destinations, token);
            }
            else
            {
                var target = names.Any(name => name.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase)) ? "shaderpacks" :
                    names.Any(name => name.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) && !names.Any(name => name.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)) ? "datapacks" :
                    "resourcepacks";
                await CopyAtomicAsync(fullSource, Path.Combine(root, target, Path.GetFileName(fullSource)), token);
                installed++;
                destinations.Add(target);
            }
        }

        return new ContentImportResult(installed, missing, destinations.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static async Task<ContentImportResult> ImportPackArchiveAsync(string root, string source,
        string? minecraftVersion, string? loaderId, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(source);
        JsonDocument? metadataDocument = null;
        var metadata = archive.GetEntry("metadata.json") ?? archive.GetEntry("modrinth.index.json");
        if (metadata is not null)
        {
            await using var stream = metadata.Open();
            metadataDocument = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            ValidateCompatibility(metadataDocument.RootElement, minecraftVersion, loaderId);
        }

        try
        {
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installed = await ExtractEntriesAsync(archive, root,
                entry => NormalizeEntry(entry.FullName).StartsWith("overrides/", StringComparison.OrdinalIgnoreCase),
                stripOverrides: true, destinations, token);

            var missing = 0;
            if (metadataDocument is not null)
            {
                var rootElement = metadataDocument.RootElement;
                missing += MissingReferences(rootElement, "mods", archive, "mods");
                missing += MissingReferences(rootElement, "resourcepacks", archive, "resourcepacks");
                missing += MissingReferences(rootElement, "shaders", archive, "shaderpacks");
                if (rootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                    missing += files.GetArrayLength();
            }
            return new ContentImportResult(installed, missing, destinations.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            metadataDocument?.Dispose();
        }
    }

    private static void ValidateCompatibility(JsonElement metadata, string? minecraftVersion, string? loaderId)
    {
        if (!string.IsNullOrWhiteSpace(minecraftVersion) &&
            metadata.TryGetProperty("gameVersion", out var gameVersion) &&
            gameVersion.ValueKind == JsonValueKind.String &&
            !string.Equals(gameVersion.GetString(), minecraftVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Este pack requiere Minecraft {gameVersion.GetString()}, pero la instancia usa {minecraftVersion}.");

        if (string.IsNullOrWhiteSpace(loaderId) || !metadata.TryGetProperty("loaders", out var loaders) || loaders.ValueKind != JsonValueKind.Array) return;
        var supported = loaders.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (supported.Length > 0 && !supported.Contains(loaderId, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Este pack requiere {string.Join(" o ", supported)}, pero la instancia usa {loaderId}.");
    }
    private static int MissingReferences(JsonElement metadata, string property, ZipArchive archive, string overrideFolder)
    {
        if (!metadata.TryGetProperty(property, out var references) || references.ValueKind != JsonValueKind.Array) return 0;
        var prefix = $"overrides/{overrideFolder}/";
        var embedded = archive.Entries
            .Select(entry => NormalizeEntry(entry.FullName))
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.Length > prefix.Length)
            .Select(path => path[prefix.Length..].Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return Math.Max(0, references.GetArrayLength() - embedded);
    }
    public async Task<int> ApplyPendingDatapacksAsync(string gameDirectory, CancellationToken token = default)
    {
        var root = NormalizeRoot(gameDirectory);
        var pending = Path.Combine(root, "datapacks");
        var saves = Path.Combine(root, "saves");
        if (!Directory.Exists(pending) || !Directory.Exists(saves)) return 0;

        var installed = 0;
        foreach (var world in Directory.EnumerateDirectories(saves).Where(path => File.Exists(Path.Combine(path, "level.dat"))))
        foreach (var datapack in Directory.EnumerateFiles(pending, "*.zip", SearchOption.TopDirectoryOnly))
        {
            await CopyAtomicAsync(datapack, Path.Combine(world, "datapacks", Path.GetFileName(datapack)), token);
            installed++;
        }
        return installed;
    }
    private static async Task<int> ExtractEntriesAsync(ZipArchive archive, string root, Func<ZipArchiveEntry, bool> include,
        bool stripOverrides, HashSet<string> destinations, CancellationToken token)
    {
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!include(entry) || string.IsNullOrEmpty(entry.Name)) continue;
            var relative = NormalizeEntry(entry.FullName);
            if (stripOverrides) relative = relative["overrides/".Length..];
            if (string.IsNullOrWhiteSpace(relative)) continue;

            var destination = SafeDestination(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination + ".nexo-import", FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
            await output.FlushAsync(token);
            output.Close();
            File.Move(destination + ".nexo-import", destination, true);
            destinations.Add(relative.Split('/')[0]);
            count++;
        }
        return count;
    }

    private static bool IsOverrideEntry(string path)
    {
        var root = path.Split('/', 2)[0];
        return OverrideRoots.Contains(root);
    }

    private static string NormalizeEntry(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string SafeDestination(string root, string relative)
    {
        if (relative.Split('/').Any(part => part is ".." or ".")) throw new InvalidDataException("El pack contiene una ruta no válida.");
        var destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("El pack intenta escribir fuera de la instancia.");
        return destination;
    }

    private static async Task CopyAtomicAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".nexo-import";
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            await input.CopyToAsync(output, token);
            await output.FlushAsync(token);
        }
        File.Move(temporary, destination, true);
    }

    private static string NormalizeRoot(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        return Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}