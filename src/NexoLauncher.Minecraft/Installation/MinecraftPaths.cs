namespace NexoLauncher.Minecraft.Installation;

public sealed class MinecraftPaths(string root, string? cacheRoot = null, string? logsRoot = null)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Cache { get; } = Path.GetFullPath(cacheRoot ?? Path.Combine(root, "cache"));
    public string Logs { get; } = Path.GetFullPath(logsRoot ?? Path.Combine(root, "logs"));
    public string Versions => Path.Combine(Root, "versions");
    public string Libraries => Path.Combine(Root, "libraries");
    public string Assets => Path.Combine(Root, "assets");
    public string VersionDirectory(string id) => Path.Combine(Versions, id);
    public string VersionJson(string id) => Path.Combine(VersionDirectory(id), id + ".json");
    public string ClientJar(string id) => Path.Combine(VersionDirectory(id), id + ".jar");
    public string Natives(string id) => Path.Combine(VersionDirectory(id), "natives");
    public string GameDirectory(string id) => Path.Combine(VersionDirectory(id), "game");
    public string FabricProfile(string minecraftVersion, string loaderVersion) =>
        Path.Combine(VersionDirectory(minecraftVersion), "loaders", "fabric", loaderVersion, "profile.json");
    public string LoaderProfile(string loaderId, string minecraftVersion, string loaderVersion) =>
        Path.Combine(VersionDirectory(minecraftVersion), "loaders", loaderId, loaderVersion, "profile.json");
    public string LoaderInstaller(string loaderId, string minecraftVersion, string loaderVersion) =>
        Path.Combine(VersionDirectory(minecraftVersion), "loaders", loaderId, loaderVersion, "installer.jar");
    public void EnsureCreated()
    {
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(Libraries);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Logs);
    }
}
