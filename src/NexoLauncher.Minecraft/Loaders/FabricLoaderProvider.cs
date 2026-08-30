using System.Text.Json;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Installation;

namespace NexoLauncher.Minecraft.Loaders;

public sealed class FabricLoaderProvider(
    FabricMetadataClient metadata,
    VanillaInstaller vanilla,
    VerifiedDownloader downloader,
    MinecraftPaths paths) : ILoaderProvider
{
    private const string DefaultMaven = "https://maven.fabricmc.net/";
    public string Id => "fabric";

    public Task<IReadOnlyList<LoaderVersion>> GetVersionsAsync(string minecraftVersion, CancellationToken token = default)
        => metadata.GetLoaderVersionsAsync(minecraftVersion, token);

    public bool IsInstalled(string minecraftVersion, string? loaderVersion)
        => !string.IsNullOrWhiteSpace(loaderVersion)
           && vanilla.IsInstalled(minecraftVersion)
           && File.Exists(paths.FabricProfile(minecraftVersion, loaderVersion));

    public async Task InstallAsync(LoaderInstallRequest request, IProgress<InstallProgress>? progress = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(request.LoaderVersion))
            throw new ArgumentException("Fabric requiere una versión de loader.", nameof(request));

        if (!vanilla.IsInstalled(request.Version.Id))
            await vanilla.InstallAsync(request.Version, progress, token);

        var profilePath = paths.FabricProfile(request.Version.Id, request.LoaderVersion);
        progress?.Report(new("Descargando perfil oficial de Fabric", 0, 1));
        await downloader.DownloadAsync(metadata.ProfileUrl(request.Version.Id, request.LoaderVersion), profilePath, null, token);

        using var profile = JsonDocument.Parse(await File.ReadAllBytesAsync(profilePath, token));
        var libraries = profile.RootElement.GetProperty("libraries").EnumerateArray().ToArray();
        var completed = 0;
        await Parallel.ForEachAsync(libraries, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (library, ct) =>
        {
            var coordinate = library.GetProperty("name").GetString()
                ?? throw new InvalidDataException("Fabric publicó una biblioteca sin coordenada Maven.");
            var resolved = FabricLibraryResolver.Resolve(coordinate);
            var baseUrl = library.TryGetProperty("url", out var url) ? url.GetString() : DefaultMaven;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Fabric publicó una URL de biblioteca no segura.");
            var downloadUrl = new Uri(uri, resolved.RelativePath).AbsoluteUri;
            var target = SafeLibraryPath(resolved.RelativePath);
            await downloader.DownloadAsync(downloadUrl, target, null, ct);
            progress?.Report(new("Descargando Fabric", Interlocked.Increment(ref completed), libraries.Length));
        });
        progress?.Report(new("Fabric listo", libraries.Length, libraries.Length));
    }

    public LaunchPlan CreateLaunchPlan(string minecraftVersion, string? loaderVersion, string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion)) throw new ArgumentException("Fabric requiere una versión de loader.", nameof(loaderVersion));
        var profilePath = paths.FabricProfile(minecraftVersion, loaderVersion);
        if (!File.Exists(profilePath)) throw new FileNotFoundException("El perfil de Fabric no está instalado.", profilePath);

        using var profile = JsonDocument.Parse(File.ReadAllBytes(profilePath));
        var root = profile.RootElement;
        var libraries = root.GetProperty("libraries").EnumerateArray()
            .Select(item => FabricLibraryResolver.Resolve(item.GetProperty("name").GetString()!).RelativePath)
            .Select(SafeLibraryPath)
            .ToArray();
        var missing = libraries.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
            throw new FileNotFoundException("La instalación de Fabric está incompleta; falta una biblioteca compartida y debe repararse.", missing);
        var jvm = ReadArguments(root, "jvm");
        var game = ReadArguments(root, "game");
        return new LaunchPlan(
            minecraftVersion,
            Path.GetFullPath(gameDirectory),
            root.GetProperty("mainClass").GetString() ?? "net.fabricmc.loader.impl.launch.knot.KnotClient",
            libraries,
            jvm,
            game);
    }

    private string SafeLibraryPath(string relative)
    {
        var target = Path.GetFullPath(Path.Combine(paths.Libraries, relative.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(paths.Libraries).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fabric publicó una ruta de biblioteca fuera de shared/libraries.");
        return target;
    }

    private static IReadOnlyList<string> ReadArguments(JsonElement root, string kind)
    {
        if (!root.TryGetProperty("arguments", out var arguments) || !arguments.TryGetProperty(kind, out var values)) return [];
        return values.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }
}
