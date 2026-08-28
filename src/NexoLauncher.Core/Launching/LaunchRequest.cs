namespace NexoLauncher.Core.Launching;

public sealed record LaunchRequest(
    string JavaExecutable,
    string WorkingDirectory,
    string MainClass,
    IReadOnlyList<string> ClassPathEntries,
    IReadOnlyList<string> GameArguments,
    int MinimumMemoryMiB,
    int MaximumMemoryMiB);
