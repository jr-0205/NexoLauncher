using System.Text.Json;

namespace NexoLauncher.Minecraft.Loaders;

public sealed class FabricMetadataClient(HttpClient http)
{
    private const string BaseUrl = "https://meta.fabricmc.net/v2/versions/loader/";

    public async Task<IReadOnlyList<LoaderVersion>> GetLoaderVersionsAsync(string minecraftVersion, CancellationToken token = default)
    {
        ValidateSegment(minecraftVersion, nameof(minecraftVersion));
        var bytes = await http.GetByteArrayAsync(BaseUrl + Uri.EscapeDataString(minecraftVersion), token);
        return ParseLoaderVersions(bytes);
    }

    public string ProfileUrl(string minecraftVersion, string loaderVersion)
    {
        ValidateSegment(minecraftVersion, nameof(minecraftVersion));
        ValidateSegment(loaderVersion, nameof(loaderVersion));
        return BaseUrl + Uri.EscapeDataString(minecraftVersion) + "/" + Uri.EscapeDataString(loaderVersion) + "/profile/json";
    }

    public static IReadOnlyList<LoaderVersion> ParseLoaderVersions(ReadOnlyMemory<byte> bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("loader"))
            .Select(loader => new LoaderVersion(
                loader.GetProperty("version").GetString() ?? string.Empty,
                loader.TryGetProperty("stable", out var stable) && stable.GetBoolean()))
            .Where(item => item.Version.Length > 0)
            .DistinctBy(item => item.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateSegment(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("La versión contiene caracteres no válidos.", parameter);
    }
}
