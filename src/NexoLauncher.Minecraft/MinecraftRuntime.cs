using System.Diagnostics;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Installation;
using NexoLauncher.Minecraft.Java;
using NexoLauncher.Minecraft.Launching;
using NexoLauncher.Minecraft.Metadata;

namespace NexoLauncher.Minecraft;

public sealed class MinecraftRuntime
{
    private readonly MojangMetadataClient metadata;
    private readonly VanillaInstaller installer;
    private readonly MinecraftLauncher launcher;

    public MinecraftRuntime(HttpClient http, string dataRoot)
    {
        var paths = new MinecraftPaths(dataRoot);
        metadata = new MojangMetadataClient(http, Path.Combine(paths.Root, "cache", "version_manifest_v2.json"));
        installer = new VanillaInstaller(new VerifiedDownloader(http), paths);
        launcher = new MinecraftLauncher(paths);
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

    public bool IsInstalled(string id) => installer.IsInstalled(id);
    public Task InstallAsync(MinecraftVersion version, IProgress<InstallProgress>? progress = null, CancellationToken token = default)
        => installer.InstallAsync(version, progress, token);
    public Process Launch(LaunchOptions options) => launcher.Launch(options);
}
