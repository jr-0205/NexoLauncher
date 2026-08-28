namespace NexoLauncher.Infrastructure.System;

public static class SystemMemory
{
    public static long GetTotalMemoryMiB()
    {
        var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        if (bytes <= 0) return 0;
        return bytes / (1024 * 1024);
    }
}
