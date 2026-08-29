using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexoLauncher.Infrastructure.Content;

public sealed record CurseForgeInstallResult(string Name, int FilesDownloaded, int OverridesInstalled);

public sealed class CurseForgePackInstaller(HttpClient http, string? apiKey = null)
{
    private const string Api = "https://api.curseforge.com/v1";
    private readonly string? key = string.IsNullOrWhiteSpace(apiKey)
        ? Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY")
        : apiKey.Trim();
    private readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };

    public bool HasDeveloperApiKey => !string.IsNullOrWhiteSpace(key);

    public static bool IsPack(string path)
    {
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        try { using var archive = ZipFile.OpenRead(path); return archive.GetEntry("manifest.json") is not null; }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException) { return false; }
    }

    public async Task<CurseForgeInstallResult> InstallAsync(string packPath, string gameDirectory, string minecraftVersion,
        string loaderId, IProgress<(int Completed, int Total)>? progress = null, CancellationToken token = default)
    {
        using var archive = ZipFile.OpenRead(packPath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("El ZIP no contiene manifest.json de CurseForge.");
        CurseForgeManifest manifest;
        await using (var stream = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync<CurseForgeManifest>(stream, json, token)
                ?? throw new InvalidDataException("El manifiesto de CurseForge está vacío.");
        Validate(manifest, minecraftVersion, loaderId);

        var requiredFiles = manifest.Files.Where(value => value.Required).DistinctBy(value => (value.ProjectId, value.FileId)).ToArray();
        if (requiredFiles.Length > 0 && string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Este export de CurseForge contiene archivos remotos. CurseForge no entrega una API key al iniciar sesión como usuario: " +
                "NEXO necesita una API key de desarrollador/3rd Party aprobada. Para desarrollo configura CURSEFORGE_API_KEY. " +
                "La versión distribuida de NEXO deberá usar una integración de servidor aprobada para no exponer esa credencial en el cliente.");

        var root = NormalizeRoot(gameDirectory);
        Directory.CreateDirectory(Path.Combine(root, "mods"));
        var completed = 0;
        foreach (var reference in requiredFiles)
        {
            token.ThrowIfCancellationRequested();
            var file = await GetFileAsync(reference.ProjectId, reference.FileId, token);
            if (string.IsNullOrWhiteSpace(file.DownloadUrl))
                file = file with { DownloadUrl = await GetDownloadUrlAsync(reference.ProjectId, reference.FileId, token) };
            if (string.IsNullOrWhiteSpace(file.DownloadUrl))
                throw new InvalidOperationException(
                    $"CurseForge no permite distribuir el archivo {reference.FileId} del proyecto {reference.ProjectId} mediante terceros.");
            await DownloadAsync(file, Path.Combine(root, "mods", SafeFileName(file.FileName)), token);
            progress?.Report((++completed, requiredFiles.Length));
        }

        var overridePrefix = string.IsNullOrWhiteSpace(manifest.Overrides) ? "overrides/" : NormalizeEntry(manifest.Overrides) + "/";
        var overrides = await ExtractOverridesAsync(archive, root, overridePrefix, token);
        return new CurseForgeInstallResult(manifest.Name ?? Path.GetFileNameWithoutExtension(packPath), completed, overrides);
    }

    private async Task<CurseForgeFile> GetFileAsync(int projectId, int fileId, CancellationToken token)
    {
        using var response = await SendApiAsync($"{Api}/mods/{projectId}/files/{fileId}", token);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var envelope = await JsonSerializer.DeserializeAsync<ApiEnvelope<CurseForgeFile>>(stream, json, token);
        return envelope?.Data ?? throw new InvalidDataException("CurseForge devolvió metadatos de archivo incompletos.");
    }

    private async Task<string?> GetDownloadUrlAsync(int projectId, int fileId, CancellationToken token)
    {
        using var response = await SendApiAsync($"{Api}/mods/{projectId}/files/{fileId}/download-url", token);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return (await JsonSerializer.DeserializeAsync<ApiEnvelope<string>>(stream, json, token))?.Data;
    }

    private async Task<HttpResponseMessage> SendApiAsync(string url, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("CurseForge API key de desarrollador no configurada.");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", key);
        request.Headers.UserAgent.ParseAdd("NexoLauncher/0.5.2");
        var response = await http.SendAsync(request, token);
        if (response.StatusCode == global::System.Net.HttpStatusCode.Forbidden || response.StatusCode == global::System.Net.HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new InvalidOperationException("La API key de CurseForge fue rechazada o no tiene acceso a esta operación.");
        }
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task DownloadAsync(CurseForgeFile file, string destination, CancellationToken token)
    {
        if (!Uri.TryCreate(file.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("CurseForge devolvió una URL de descarga no HTTPS.");

        var temporary = destination + ".download";
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
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

            var hash = file.Hashes.FirstOrDefault(value => value.Algo == 1) ?? file.Hashes.FirstOrDefault(value => value.Algo == 2);
            if (hash is not null)
            {
                await using var input = File.OpenRead(temporary);
                var actual = hash.Algo == 1
                    ? Convert.ToHexString(await SHA1.HashDataAsync(input, token))
                    : Convert.ToHexString(await MD5.HashDataAsync(input, token));
                if (!actual.Equals(hash.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{file.FileName} no superó la verificación de integridad.");
            }
            File.Move(temporary, destination, true);
        }
        finally { TryDelete(temporary); }
    }

    private static async Task<int> ExtractOverridesAsync(ZipArchive archive, string root, string prefix, CancellationToken token)
    {
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = NormalizeEntry(entry.FullName);
            if (string.IsNullOrEmpty(entry.Name) || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = name[prefix.Length..];
            var destination = SafeDestination(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + ".nexo-import";
            try
            {
                await using var source = entry.Open();
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await source.CopyToAsync(output, token);
                    await output.FlushAsync(token);
                }
                File.Move(temporary, destination, true);
            }
            finally { TryDelete(temporary); }
            count++;
        }
        return count;
    }

    private static void Validate(CurseForgeManifest manifest, string minecraftVersion, string loaderId)
    {
        if (!string.Equals(manifest.Minecraft.Version, minecraftVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Este pack requiere Minecraft {manifest.Minecraft.Version}, pero la instancia usa {minecraftVersion}.");
        var primary = manifest.Minecraft.ModLoaders.FirstOrDefault(value => value.Primary) ?? manifest.Minecraft.ModLoaders.FirstOrDefault();
        var declared = primary?.Id.Split('-', 2)[0];
        if (!string.IsNullOrWhiteSpace(declared) && !string.Equals(declared, loaderId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Este pack requiere {declared}, pero la instancia usa {loaderId}.");
    }

    private static string SafeDestination(string root, string relative)
    {
        relative = NormalizeEntry(relative);
        var platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(platformRelative) ||
            relative.Split('/').Any(value => value is ".." or "." || value.Contains(':')))
            throw new InvalidDataException("El pack contiene una ruta override no válida.");
        var destination = Path.GetFullPath(Path.Combine(root, platformRelative));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El pack intenta escribir fuera de la instancia.");
        return destination;
    }

    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("CurseForge devolvió un nombre de archivo no válido.");
        return name;
    }

    private static string NormalizeEntry(string value) => value.Replace('\\', '/').Trim('/');
    private static string NormalizeRoot(string value) =>
        Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ApiEnvelope<T>(T Data);
    private sealed record CurseForgeManifest(string? Name, MinecraftManifest Minecraft, IReadOnlyList<FileReference> Files, string? Overrides);
    private sealed record MinecraftManifest(string Version, IReadOnlyList<ModLoaderReference> ModLoaders);
    private sealed record ModLoaderReference(string Id, bool Primary);
    private sealed record FileReference([property: JsonPropertyName("projectID")] int ProjectId, [property: JsonPropertyName("fileID")] int FileId, bool Required);
    private sealed record CurseForgeFile(int Id, string FileName, string? DownloadUrl, IReadOnlyList<CurseForgeHash> Hashes);
    private sealed record CurseForgeHash(string Value, int Algo);
}
