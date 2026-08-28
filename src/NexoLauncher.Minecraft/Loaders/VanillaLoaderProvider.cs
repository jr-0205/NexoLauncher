using NexoLauncher.Minecraft.Installation;

namespace NexoLauncher.Minecraft.Loaders;

public sealed class VanillaLoaderProvider(VanillaInstaller installer) : ILoaderProvider
{
    public string Id => "vanilla";

    public Task<IReadOnlyList<LoaderVersion>> GetVersionsAsync(string minecraftVersion, CancellationToken token = default)
        => Task.FromResult<IReadOnlyList<LoaderVersion>>([new("Vanilla", true)]);

    public bool IsInstalled(string minecraftVersion, string? loaderVersion) => installer.IsInstalled(minecraftVersion);

    public Task InstallAsync(LoaderInstallRequest request, IProgress<InstallProgress>? progress = null, CancellationToken token = default)
        => installer.InstallAsync(request.Version, progress, token);

    public LaunchPlan CreateLaunchPlan(string minecraftVersion, string? loaderVersion, string gameDirectory)
        => new(minecraftVersion, Path.GetFullPath(gameDirectory));
}
