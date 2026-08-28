using System.Diagnostics;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Installation;
using NexoLauncher.Minecraft.Java;
using NexoLauncher.Minecraft.Launching;
using NexoLauncher.Minecraft.Loaders;
using NexoLauncher.Minecraft.Metadata;

namespace NexoLauncher.Minecraft;

public sealed class MinecraftRuntime
{
    private readonly MojangMetadataClient metadata;
    private readonly MinecraftLauncher launcher;
    private readonly IReadOnlyDictionary<string, ILoaderProvider> loaders;

    public MinecraftRuntime(HttpClient http, string dataRoot)
    {
        var paths = new MinecraftPaths(dataRoot);
        metadata = new MojangMetadataClient(http, Path.Combine(paths.Root, "cache", "version_manifest_v2.json"));
        var downloader = new VerifiedDownloader(http);
        var installer = new VanillaInstaller(downloader, paths);
        launcher = new MinecraftLauncher(paths);
        var installerMetadata = new InstallerLoaderMetadataClient(http);
        ILoaderProvider[] providers =
        [
            new VanillaLoaderProvider(installer),
            new FabricLoaderProvider(new FabricMetadataClient(http), installer, downloader, paths),
            new InstallerLoaderProvider("forge", installerMetadata, installer, downloader, paths),
            new InstallerLoaderProvider("neoforge", installerMetadata, installer, downloader, paths)
        ];
        loaders = providers.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<MinecraftVersion>> GetReleaseVersionsAsync(CancellationToken token = default)
        => metadata.GetReleaseVersionsAsync(token);

    public async Task<int?> GetRequiredJavaMajorAsync(MinecraftVersion version, CancellationToken token = default)
    {
        try
        {
            return await metadata.GetRequiredJavaMajorAsync(version, token)
                   ?? MinecraftJavaVersionPolicy.InferRequiredMajor(version.Id);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return MinecraftJavaVersionPolicy.InferRequiredMajor(version.Id);
        }
    }

    public IReadOnlyList<string> LoaderIds => loaders.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public Task<IReadOnlyList<LoaderVersion>> GetLoaderVersionsAsync(string loaderId, string minecraftVersion, CancellationToken token = default)
        => Provider(loaderId).GetVersionsAsync(minecraftVersion, token);

    public bool IsInstalled(string id) => IsInstalled(id, "vanilla", null);
    public bool IsInstalled(string minecraftVersion, string loaderId, string? loaderVersion)
        => Provider(loaderId).IsInstalled(minecraftVersion, loaderVersion);

    public Task InstallAsync(MinecraftVersion version, IProgress<InstallProgress>? progress = null, CancellationToken token = default)
        => InstallAsync(new LoaderInstallRequest(version, null), "vanilla", progress, token);

    public Task InstallAsync(LoaderInstallRequest request, string loaderId, IProgress<InstallProgress>? progress = null, CancellationToken token = default)
        => Provider(loaderId).InstallAsync(request, progress, token);

    public LaunchPlan CreateLaunchPlan(string minecraftVersion, string loaderId, string? loaderVersion, string gameDirectory)
        => Provider(loaderId).CreateLaunchPlan(minecraftVersion, loaderVersion, gameDirectory);

    public Process Launch(LaunchOptions options) => launcher.Launch(options);
    public Process Launch(LaunchOptions options, LaunchPlan plan) => launcher.Launch(options, plan);

    private ILoaderProvider Provider(string loaderId)
    {
        if (loaders.TryGetValue(loaderId, out var provider)) return provider;
        throw new NotSupportedException($"El loader '{loaderId}' todavía no es compatible con NEXO.");
    }
}
