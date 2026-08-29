using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexoLauncher.Infrastructure.Content;

public sealed record ContentImportResult(int FilesInstalled, int ReferencedFilesMissing, IReadOnlyList<string> Destinations)
{
    public static ContentImportResult Empty { get; } = new(0, 0, []);
}

public sealed class InstanceContentManager
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(20) };
    private static readonly HashSet<string> OverrideRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "mods", "resourcepacks", "shaderpacks", "datapacks", "config", "defaultconfigs", "kubejs", "scripts",
        "saves", "screenshots", "logs", "crash-reports", "options.txt", "servers.dat", "journeymap", "xaero",
        "rewind", "showdown"
    };

    private readonly HttpClient http;

    public InstanceContentManager(HttpClient? http = null) => this.http = http ?? SharedHttp;

    public void EnsureLayout(string gameDirectory)
    {
        var root = NormalizeRoot(gameDirectory);
        foreach (var folder in new[]
                 {
                     "mods", "resourcepacks", "shaderpacks", "datapacks", "config", "defaultconfigs", "kubejs",
                     "saves", "screenshots", "logs", "crash-reports"
                 })
            Directory.CreateDirectory(Path.Combine(root, folder));
    }

    public async Task<ContentImportResult> ImportAsync(string gameDirectory, IEnumerable<string> sourcePaths,
        string? minecraftVersion = null, string? loaderId = null, CancellationToken token = default)
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

            if (extension.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                var result = await ImportModrinthPackAsync(root, fullSource, minecraftVersion, loaderId, token);
                installed += result.FilesInstalled;
                missing += result.ReferencedFilesMissing;
                foreach (var destination in result.Destinations) destinations.Add(destination);
                continue;
            }

            if (extension.Equals(".lcpack", StringComparison.OrdinalIgnoreCase))
            {
                var result = await ImportLunarPackArchiveAsync(root, fullSource, minecraftVersion, loaderId, token);
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
                installed += await ExtractEntriesAsync(archive, root, entry => IsOverrideEntry(NormalizeEntry(entry.FullName)),
                    stripPrefix: null, destinations, token);
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

    private async Task<ContentImportResult> ImportModrinthPackAsync(string root, string source,
        string? minecraftVersion, string? loaderId, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(source);
        var indexEntry = archive.GetEntry("modrinth.index.json")
            ?? throw new InvalidDataException("El .mrpack no contiene modrinth.index.json.");

        JsonDocument document;
        await using (var stream = indexEntry.Open())
            document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        using (document)
        {
            var index = document.RootElement;
            ValidateModrinthCompatibility(index, minecraftVersion, loaderId);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installed = 0;

            installed += await ExtractEntriesAsync(archive, root,
                entry => NormalizeEntry(entry.FullName).StartsWith("overrides/", StringComparison.OrdinalIgnoreCase),
                "overrides/", destinations, token);
            installed += await ExtractEntriesAsync(archive, root,
                entry => NormalizeEntry(entry.FullName).StartsWith("client-overrides/", StringComparison.OrdinalIgnoreCase),
                "client-overrides/", destinations, token);

            if (!index.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return new ContentImportResult(installed, 0, destinations.Order(StringComparer.OrdinalIgnoreCase).ToArray());

            foreach (var file in files.EnumerateArray())
            {
                token.ThrowIfCancellationRequested();
                if (!ShouldInstallOnClient(file)) continue;
                var relative = file.GetProperty("path").GetString()
                    ?? throw new InvalidDataException("El .mrpack contiene un archivo sin ruta.");
                var destination = SafeDestination(root, relative);
                var hashes = file.TryGetProperty("hashes", out var hashObject) && hashObject.ValueKind == JsonValueKind.Object
                    ? hashObject
                    : default;
                var sha512 = hashes.ValueKind == JsonValueKind.Object && hashes.TryGetProperty("sha512", out var sha512Value)
                    ? sha512Value.GetString()
                    : null;
                var sha1 = hashes.ValueKind == JsonValueKind.Object && hashes.TryGetProperty("sha1", out var sha1Value)
                    ? sha1Value.GetString()
                    : null;

                if (!file.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"El archivo '{relative}' no contiene URLs de descarga.");
                var urls = downloads.EnumerateArray()
                    .Where(value => value.ValueKind == JsonValueKind.String)
                    .Select(value => value.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray();
                if (urls.Length == 0) throw new InvalidDataException($"El archivo '{relative}' no contiene URLs de descarga.");

                await DownloadVerifiedAsync(urls, destination, sha512, sha1, token);
                installed++;
                destinations.Add(TopLevel(relative));
            }

            return new ContentImportResult(installed, 0, destinations.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private static async Task<ContentImportResult> ImportLunarPackArchiveAsync(string root, string source,
        string? minecraftVersion, string? loaderId, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(source);
        JsonDocument? metadataDocument = null;
        var metadata = archive.GetEntry("metadata.json");
        if (metadata is not null)
        {
            await using var stream = metadata.Open();
            metadataDocument = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            ValidateLegacyPackCompatibility(metadataDocument.RootElement, minecraftVersion, loaderId);
        }

        try
        {
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var installed = await ExtractEntriesAsync(archive, root,
                entry => NormalizeEntry(entry.FullName).StartsWith("overrides/", StringComparison.OrdinalIgnoreCase),
                "overrides/", destinations, token);

            var missing = 0;
            if (metadataDocument is not null)
            {
                var rootElement = metadataDocument.RootElement;
                missing += MissingReferences(rootElement, "mods", archive, "mods");
                missing += MissingReferences(rootElement, "resourcepacks", archive, "resourcepacks");
                missing += MissingReferences(rootElement, "shaders", archive, "shaderpacks");
            }
            return new ContentImportResult(installed, missing, destinations.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        finally
        {
            metadataDocument?.Dispose();
        }
    }

    private static void ValidateModrinthCompatibility(JsonElement index, string? minecraftVersion, string? loaderId)
    {
        if (!index.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object) return;

        if (!string.IsNullOrWhiteSpace(minecraftVersion) &&
            dependencies.TryGetProperty("minecraft", out var minecraft) && minecraft.ValueKind == JsonValueKind.String &&
            !string.Equals(minecraft.GetString(), minecraftVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Este modpack requiere Minecraft {minecraft.GetString()}, pero la instancia usa {minecraftVersion}.");

        if (string.IsNullOrWhiteSpace(loaderId)) return;
        var declaredLoaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["fabric-loader"] = "fabric",
            ["forge"] = "forge",
            ["neoforge"] = "neoforge",
            ["quilt-loader"] = "quilt"
        };
        var required = declaredLoaders
            .Where(pair => dependencies.TryGetProperty(pair.Key, out var value) && value.ValueKind == JsonValueKind.String)
            .Select(pair => pair.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (required.Length > 0 && !required.Contains(loaderId, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"Este modpack requiere {string.Join(" o ", required)}, pero la instancia usa {loaderId}.");
    }

    private static void ValidateLegacyPackCompatibility(JsonElement metadata, string? minecraftVersion, string? loaderId)
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

    private static bool ShouldInstallOnClient(JsonElement file)
    {
        if (!file.TryGetProperty("env", out var env) || env.ValueKind != JsonValueKind.Object) return true;
        return !env.TryGetProperty("client", out var client) || client.ValueKind != JsonValueKind.String ||
               !string.Equals(client.GetString(), "unsupported", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DownloadVerifiedAsync(IReadOnlyList<string> urls, string destination, string? sha512, string? sha1,
        CancellationToken token)
    {
        if (File.Exists(destination) && await HasExpectedHashAsync(destination, sha512, sha1, token)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Exception? lastError = null;

        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                lastError = new InvalidDataException("Los .mrpack solo pueden descargar archivos mediante HTTPS.");
                continue;
            }

            var temporary = destination + ".nexo-download";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd("NexoLauncher/0.5.2");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await input.CopyToAsync(output, token);
                    await output.FlushAsync(token);
                }
                if (!await HasExpectedHashAsync(temporary, sha512, sha1, token))
                    throw new InvalidDataException($"La descarga de '{Path.GetFileName(destination)}' no superó la verificación de integridad.");
                File.Move(temporary, destination, true);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
            {
                lastError = exception;
                TryDelete(temporary);
            }
        }

        throw new InvalidOperationException($"No se pudo descargar '{Path.GetFileName(destination)}' desde ninguna URL del .mrpack.", lastError);
    }

    private static async Task<bool> HasExpectedHashAsync(string path, string? sha512, string? sha1, CancellationToken token)
    {
        if (!File.Exists(path)) return false;
        if (!string.IsNullOrWhiteSpace(sha512))
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA512.HashDataAsync(stream, token));
            return actual.Equals(sha512, StringComparison.OrdinalIgnoreCase);
        }
        if (!string.IsNullOrWhiteSpace(sha1))
        {
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA1.HashDataAsync(stream, token));
            return actual.Equals(sha1, StringComparison.OrdinalIgnoreCase);
        }
        throw new InvalidDataException("Un archivo remoto del .mrpack no incluye SHA-512 ni SHA-1.");
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
        string? stripPrefix, HashSet<string> destinations, CancellationToken token)
    {
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (!include(entry) || string.IsNullOrEmpty(entry.Name)) continue;
            var relative = NormalizeEntry(entry.FullName);
            if (!string.IsNullOrWhiteSpace(stripPrefix))
            {
                if (!relative.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                relative = relative[stripPrefix.Length..];
            }
            if (string.IsNullOrWhiteSpace(relative)) continue;

            var destination = SafeDestination(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".nexo-import";
            try
            {
                await using var input = entry.Open();
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await input.CopyToAsync(output, token);
                    await output.FlushAsync(token);
                }
                File.Move(temporary, destination, true);
            }
            finally { TryDelete(temporary); }
            destinations.Add(TopLevel(relative));
            count++;
        }
        return count;
    }

    private static bool IsOverrideEntry(string path)
    {
        var root = path.Split('/', 2)[0];
        return OverrideRoots.Contains(root);
    }

    private static string TopLevel(string path) => NormalizeEntry(path).Split('/', 2)[0];
    private static string NormalizeEntry(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string SafeDestination(string root, string relative)
    {
        relative = NormalizeEntry(relative);
        var platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(platformRelative) ||
            relative.Split('/').Any(part => part is ".." or "." || part.Contains(':')))
            throw new InvalidDataException("El pack contiene una ruta no válida.");
        var destination = Path.GetFullPath(Path.Combine(root, platformRelative));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El pack intenta escribir fuera de la instancia.");
        return destination;
    }

    private static async Task CopyAtomicAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".nexo-import";
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, token);
                await output.FlushAsync(token);
            }
            File.Move(temporary, destination, true);
        }
        finally { TryDelete(temporary); }
    }

    private static string NormalizeRoot(string gameDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        return Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
