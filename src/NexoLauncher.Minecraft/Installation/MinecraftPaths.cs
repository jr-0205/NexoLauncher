namespace NexoLauncher.Minecraft.Installation;

public sealed class MinecraftPaths(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Instances => Path.Combine(Root, "instances");
    public string Libraries => Path.Combine(Root, "libraries");
    public string Assets => Path.Combine(Root, "assets");
    public string VersionDirectory(string id) => Path.Combine(Instances, id);
    public string VersionJson(string id) => Path.Combine(VersionDirectory(id), id + ".json");
    public string ClientJar(string id) => Path.Combine(VersionDirectory(id), id + ".jar");
    public string Natives(string id) => Path.Combine(VersionDirectory(id), "natives");
    public string GameDirectory(string id) => Path.Combine(VersionDirectory(id), "game");
    public string FabricProfile(string minecraftVersion, string loaderVersion) =>
        Path.Combine(VersionDirectory(minecraftVersion), "loaders", "fabric", loaderVersion, "profile.json");
    public void EnsureCreated() { Directory.CreateDirectory(Instances); Directory.CreateDirectory(Libraries); Directory.CreateDirectory(Assets); }
}
