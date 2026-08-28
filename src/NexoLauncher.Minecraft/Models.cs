namespace NexoLauncher.Minecraft;

public sealed record MinecraftVersion(string Id, DateTimeOffset ReleaseTime, string MetadataUrl)
{
    public override string ToString() => $"{Id}  ·  {ReleaseTime:yyyy-MM-dd}";
}

public sealed record InstallProgress(string Stage, int Completed, int Total)
{
    public double Percentage => Total == 0 ? 0 : Completed * 100d / Total;
}

public sealed record LaunchOptions(
    string VersionId,
    string JavaExecutable,
    string Username,
    int MemoryMiB,
    string? AccountId = null,
    string? AccessToken = null);

public sealed record DownloadJob(string Url, string Path, string? Sha1, bool IsNative = false);
