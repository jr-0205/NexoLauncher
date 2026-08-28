namespace NexoLauncher.Minecraft.Loaders;

public interface ILoaderProvider
{
    string Id { get; }
    Task<IReadOnlyList<LoaderVersion>> GetVersionsAsync(string minecraftVersion, CancellationToken token = default);
    bool IsInstalled(string minecraftVersion, string? loaderVersion);
    Task InstallAsync(LoaderInstallRequest request, IProgress<InstallProgress>? progress = null, CancellationToken token = default);
    LaunchPlan CreateLaunchPlan(string minecraftVersion, string? loaderVersion, string gameDirectory);
}
