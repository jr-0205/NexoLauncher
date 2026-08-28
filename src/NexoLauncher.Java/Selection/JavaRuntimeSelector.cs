namespace NexoLauncher.Java.Selection;

public static class JavaRuntimeSelector
{
    public static JavaRuntime? Select(IReadOnlyList<JavaRuntime> runtimes, int? requiredMajor)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        IEnumerable<JavaRuntime> candidates = runtimes;

        if (requiredMajor is > 0)
            candidates = candidates.Where(runtime => runtime.MajorVersion == requiredMajor.Value);

        if (Environment.Is64BitOperatingSystem)
            candidates = candidates.Where(runtime => runtime.Is64Bit);

        return candidates
            .OrderByDescending(runtime => runtime.MajorVersion)
            .ThenBy(runtime => SourceRank(runtime.Source))
            .ThenBy(runtime => runtime.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(runtime => runtime.JavaExecutable, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static IReadOnlyList<int> DetectedMajors(IReadOnlyList<JavaRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        return runtimes
            .Select(runtime => runtime.MajorVersion)
            .Where(major => major > 0)
            .Distinct()
            .OrderBy(major => major)
            .ToArray();
    }

    private static int SourceRank(string source) => source switch
    {
        "JAVA_HOME" => 0,
        "Program Files" => 1,
        "PATH" => 2,
        "Manual" => 3,
        _ => 4
    };
}
