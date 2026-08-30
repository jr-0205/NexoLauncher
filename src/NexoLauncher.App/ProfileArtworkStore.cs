using System.Security.Cryptography;
using System.Text.Json;

namespace NexoLauncher.App;

internal sealed record ProfileArtwork(
    string? IconRelativePath,
    string? BackgroundRelativePath,
    string? IconSha256,
    string? BackgroundSha256);

internal static class ProfileArtworkStore
{
    private const string ProfileDirectoryName = "profile";
    private const string MetadataFileName = "artwork.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<ProfileArtwork> ImportAsync(
        string instanceRoot,
        string? iconSource,
        string? backgroundSource,
        CancellationToken token)
    {
        var root = Path.GetFullPath(instanceRoot);
        var profileDirectory = Path.Combine(root, ProfileDirectoryName);
        Directory.CreateDirectory(profileDirectory);

        var icon = await ImportOneAsync(root, profileDirectory, iconSource, "icon", token);
        var background = await ImportOneAsync(root, profileDirectory, backgroundSource, "background", token);
        return new ProfileArtwork(icon.RelativePath, background.RelativePath, icon.Sha256, background.Sha256);
    }

    public static async Task SaveMetadataAsync(string instanceRoot, ProfileArtwork artwork, CancellationToken token)
    {
        var root = Path.GetFullPath(instanceRoot);
        var directory = Path.Combine(root, ProfileDirectoryName);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, MetadataFileName);
        var temporary = destination + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, artwork, JsonOptions, token);
            await stream.FlushAsync(token);
        }
        File.Move(temporary, destination, true);
    }

    public static ProfileArtwork? Load(string instanceRoot)
    {
        var metadata = Path.Combine(Path.GetFullPath(instanceRoot), ProfileDirectoryName, MetadataFileName);
        if (!File.Exists(metadata)) return null;
        try
        {
            return JsonSerializer.Deserialize<ProfileArtwork>(File.ReadAllBytes(metadata), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static string? Resolve(string instanceRoot, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var root = Path.GetFullPath(instanceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate) ? candidate : null;
    }

    private static async Task<(string? RelativePath, string? Sha256)> ImportOneAsync(
        string root,
        string profileDirectory,
        string? source,
        string baseName,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(source)) return (null, null);
        var fullSource = Path.GetFullPath(source);
        if (!File.Exists(fullSource)) throw new FileNotFoundException("No se encontró la imagen seleccionada para el perfil.", fullSource);
        var extension = Path.GetExtension(fullSource).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp")
            throw new InvalidDataException("NEXO acepta PNG, JPG, JPEG o BMP como imágenes de perfil.");

        var destination = Path.Combine(profileDirectory, baseName + extension);
        var temporary = destination + ".tmp";
        try
        {
            await using (var input = new FileStream(fullSource, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await input.CopyToAsync(output, token);
                await output.FlushAsync(token);
            }
            File.Move(temporary, destination, true);
            await using var hashStream = File.OpenRead(destination);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, token)).ToLowerInvariant();
            var relative = Path.GetRelativePath(root, destination).Replace(Path.DirectorySeparatorChar, '/');
            return (relative, hash);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
