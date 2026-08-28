namespace NexoLauncher.Domain.Configuration;

public sealed record LauncherSettings(
    int MemoryMiB = 4096,
    string? JavaPath = null,
    string Username = "Player",
    bool CloseLauncherOnGameStart = true)
{
    public LauncherSettings Normalize()
    {
        var username = string.IsNullOrWhiteSpace(Username) ? "Player" : Username.Trim();
        if (username.Length > 16) username = username[..16];

        return this with
        {
            MemoryMiB = Math.Clamp(MemoryMiB, 1024, 32768),
            JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath.Trim(),
            Username = username
        };
    }
}
