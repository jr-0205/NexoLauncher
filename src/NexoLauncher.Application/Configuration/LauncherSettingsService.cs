using NexoLauncher.Domain.Configuration;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Application.Configuration;

public interface ILauncherSettingsStore
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
}

public sealed record ResolvedInstanceSettings(
    int MemoryMiB,
    string? JavaPath,
    IReadOnlyList<string> JvmArguments,
    int? WindowWidth,
    int? WindowHeight,
    bool? Fullscreen);

public static class LauncherSettingsResolver
{
    public static ResolvedInstanceSettings Resolve(LauncherSettings global, InstanceSettings instance)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(instance);

        var normalized = global.Normalize();
        return new ResolvedInstanceSettings(
            instance.MemoryMiB is > 0 ? instance.MemoryMiB.Value : normalized.MemoryMiB,
            string.IsNullOrWhiteSpace(instance.JavaPath) ? normalized.JavaPath : instance.JavaPath,
            instance.JvmArguments ?? [],
            instance.WindowWidth,
            instance.WindowHeight,
            instance.Fullscreen);
    }
}

public static class MemoryRecommendation
{
    public const int MinimumMiB = 1024;
    public const int MaximumMiB = 32768;
    public const int ReservedForWindowsMiB = 2048;

    public static int RecommendMiB(long totalMemoryMiB)
    {
        if (totalMemoryMiB <= 0) return 4096;
        if (totalMemoryMiB <= 4096) return 2048;
        if (totalMemoryMiB <= 8192) return 3072;
        if (totalMemoryMiB <= 16384) return 4096;
        if (totalMemoryMiB <= 32768) return 6144;
        return 8192;
    }

    public static int SafeMaximumMiB(long totalMemoryMiB)
    {
        if (totalMemoryMiB <= 0) return 8192;
        var reserved = Math.Max(MinimumMiB, totalMemoryMiB - ReservedForWindowsMiB);
        return Math.Clamp((int)Math.Min(int.MaxValue, reserved), MinimumMiB, MaximumMiB);
    }
}
