namespace NexoLauncher.Core.Installation;

public sealed record NexoPaths(string Root)
{
    public string Instances => Path.Combine(Root, "instances");
    public string Versions => Path.Combine(Root, "versions");
    public string Runtime => Path.Combine(Root, "runtime");
    public string Cache => Path.Combine(Root, "cache");
    public string Logs => Path.Combine(Root, "logs");

    public static NexoPaths ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoLauncher"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Instances);
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(Runtime);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Logs);
    }
}
