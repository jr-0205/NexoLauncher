using System.Diagnostics;

namespace NexoLauncher.Core.Launching;

public static class MinecraftProcessFactory
{
    public static ProcessStartInfo CreateStartInfo(LaunchRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.JavaExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MainClass);
        if (request.ClassPathEntries.Count == 0)
        {
            throw new ArgumentException("Minecraft requires at least one classpath entry.", nameof(request));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.JavaExecutable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add($"-Xms{request.MinimumMemoryMiB}M");
        startInfo.ArgumentList.Add($"-Xmx{request.MaximumMemoryMiB}M");
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(string.Join(Path.PathSeparator, request.ClassPathEntries));
        startInfo.ArgumentList.Add(request.MainClass);
        foreach (var argument in request.GameArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
