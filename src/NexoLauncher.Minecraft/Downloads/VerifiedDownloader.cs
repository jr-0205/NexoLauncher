using System.Security.Cryptography;

namespace NexoLauncher.Minecraft.Downloads;

public sealed class VerifiedDownloader(HttpClient http)
{
    public async Task DownloadAsync(string url, string destination, string? expectedSha1, CancellationToken token = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("NEXO solo permite descargas HTTPS.");
        if (File.Exists(destination) && (expectedSha1 is null || await HasSha1Async(destination, expectedSha1, token))) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".download";
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await input.CopyToAsync(output, token);
            if (expectedSha1 is not null && !await HasSha1Async(temporary, expectedSha1, token))
                throw new InvalidDataException($"El hash SHA-1 no coincide para {uri.Host}.");
            File.Move(temporary, destination, true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public static async Task<bool> HasSha1Async(string path, string expected, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA1.HashDataAsync(stream, token)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
