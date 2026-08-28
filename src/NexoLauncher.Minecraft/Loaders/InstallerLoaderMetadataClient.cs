namespace NexoLauncher.Minecraft.Loaders;

public sealed class InstallerLoaderMetadataClient(HttpClient http)
{
    private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

    public async Task<IReadOnlyList<LoaderVersion>> GetForgeVersionsAsync(string minecraftVersion, CancellationToken token = default)
    {
        Validate(minecraftVersion);
        var versions = MavenMetadataParser.ParseVersions(await http.GetByteArrayAsync(ForgeMetadata, token));
        var prefix = minecraftVersion + "-";
        return versions.Where(value => value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(value => new LoaderVersion(value[prefix.Length..], !IsPreview(value)))
            .Reverse().ToArray();
    }

    public async Task<IReadOnlyList<LoaderVersion>> GetNeoForgeVersionsAsync(string minecraftVersion, CancellationToken token = default)
    {
        Validate(minecraftVersion);
        var prefix = NeoForgePrefix(minecraftVersion);
        if (prefix is null) return [];
        var versions = MavenMetadataParser.ParseVersions(await http.GetByteArrayAsync(NeoForgeMetadata, token));
        return versions.Where(value => value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(value => new LoaderVersion(value, !IsPreview(value)))
            .Reverse().ToArray();
    }

    public static string? NeoForgePrefix(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length < 2 || parts[0] != "1" || !int.TryParse(parts[1], out var minor) || minor < 20) return null;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var parsed) ? parsed : 0;
        return $"{minor}.{patch}.";
    }

    public static string ForgeInstallerUrl(string minecraftVersion, string loaderVersion)
    {
        Validate(minecraftVersion); Validate(loaderVersion);
        var version = $"{minecraftVersion}-{loaderVersion}";
        return $"https://maven.minecraftforge.net/net/minecraftforge/forge/{version}/forge-{version}-installer.jar";
    }

    public static string NeoForgeInstallerUrl(string loaderVersion)
    {
        Validate(loaderVersion);
        return $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVersion}/neoforge-{loaderVersion}-installer.jar";
    }

    public async Task<string> GetSha1Async(string artifactUrl, CancellationToken token = default)
    {
        if (!Uri.TryCreate(artifactUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("La URL del instalador no es HTTPS.", nameof(artifactUrl));
        var checksum = (await http.GetStringAsync(artifactUrl + ".sha1", token)).Trim();
        if (checksum.Length != 40 || checksum.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("El repositorio del loader publicó un SHA-1 no válido.");
        return checksum.ToLowerInvariant();
    }

    private static bool IsPreview(string value) => value.Contains("beta", StringComparison.OrdinalIgnoreCase)
        || value.Contains("alpha", StringComparison.OrdinalIgnoreCase) || value.Contains("rc", StringComparison.OrdinalIgnoreCase);

    private static void Validate(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-' and not '+'))
            throw new ArgumentException("La versión contiene caracteres no válidos.");
    }
}
