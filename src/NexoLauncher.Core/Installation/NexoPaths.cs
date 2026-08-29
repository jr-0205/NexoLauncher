namespace NexoLauncher.Core.Installation;

public sealed record NexoPaths(string Root, string LunarRoot)
{
    public string Instances => Path.Combine(LunarRoot, "profiles");
    public string Runtime => Path.Combine(LunarRoot, "jre");
    public string Cache => Path.Combine(LunarRoot, "cache", "nexo");
    public string Logs => Path.Combine(LunarRoot, "logs", "nexo");

    public static NexoPaths ForCurrentUser()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var lunarRoot = Path.Combine(userProfile, ".lunarclient");
        return new NexoPaths(Path.Combine(lunarRoot, "nexo"), lunarRoot);
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(LunarRoot);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Instances);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Logs);
    }
}
