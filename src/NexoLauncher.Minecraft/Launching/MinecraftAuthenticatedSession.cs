namespace NexoLauncher.Minecraft.Launching;

/// <summary>
/// Ephemeral, process-only handoff for an authenticated Minecraft identity.
/// The bearer token is never persisted here and is consumed exactly once, immediately before process creation.
/// </summary>
public static class MinecraftAuthenticatedSession
{
    private static readonly object Gate = new();
    private static Identity? current;

    public static void Set(string accountId, string username, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        lock (Gate) current = new Identity(accountId, username, accessToken);
    }

    public static void Clear()
    {
        lock (Gate) current = null;
    }

    public static LaunchOptions Apply(LaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (Gate)
        {
            if (current is null) return options;
            var identity = current;
            current = null;
            return options with
            {
                Username = identity.Username,
                AccountId = identity.AccountId,
                AccessToken = identity.AccessToken
            };
        }
    }

    private sealed record Identity(string AccountId, string Username, string AccessToken);
}
