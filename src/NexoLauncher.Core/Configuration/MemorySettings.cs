namespace NexoLauncher.Core.Configuration;

public sealed record MemorySettings(int MinimumMiB, int MaximumMiB)
{
    public const int AbsoluteMinimumMiB = 512;
    public const int ReservedForWindowsMiB = 2048;

    public static MemorySettings CreateSafe(int requestedMaximumMiB, long totalPhysicalMemoryMiB)
    {
        if (totalPhysicalMemoryMiB <= AbsoluteMinimumMiB)
        {
            throw new ArgumentOutOfRangeException(nameof(totalPhysicalMemoryMiB));
        }

        var safeCeiling = (int)Math.Min(int.MaxValue, Math.Max(AbsoluteMinimumMiB, totalPhysicalMemoryMiB - ReservedForWindowsMiB));
        var maximum = Math.Clamp(requestedMaximumMiB, AbsoluteMinimumMiB, safeCeiling);
        var minimum = Math.Min(AbsoluteMinimumMiB, maximum);
        return new MemorySettings(minimum, maximum);
    }
}
