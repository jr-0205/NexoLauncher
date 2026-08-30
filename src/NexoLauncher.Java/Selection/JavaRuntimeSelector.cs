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
            .OrderByDescending(runtime => ParseVersion(runtime.FullVersion))
            .ThenBy(runtime => SourceRank(runtime.Source))
            .ThenBy(runtime => runtime.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(runtime => runtime.JavaExecutable, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static IReadOnlyList<JavaRuntime> BestPerMajor(IReadOnlyList<JavaRuntime> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);

        return runtimes
            .Where(runtime => runtime.MajorVersion > 0)
            .GroupBy(runtime => runtime.MajorVersion)
            .Select(group => Select(group.ToArray(), group.Key))
            .Where(runtime => runtime is not null)
            .Cast<JavaRuntime>()
            .OrderBy(runtime => runtime.MajorVersion)
            .ToArray();
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

    private static Version ParseVersion(string fullVersion)
    {
        if (string.IsNullOrWhiteSpace(fullVersion)) return new Version(0, 0);

        var normalized = fullVersion.Trim();
        var suffix = normalized.IndexOfAny(['+', '-']);
        if (suffix >= 0) normalized = normalized[..suffix];
        normalized = normalized.Replace('_', '.');

        return Version.TryParse(normalized, out var parsed)
            ? parsed
            : new Version(0, 0);
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
