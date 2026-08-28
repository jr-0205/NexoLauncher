namespace NexoLauncher.Core.Java;

public sealed class JavaDetector
{
    public IReadOnlyList<JavaInstallation> Detect()
    {
        var candidates = new List<JavaInstallation>();
        AddFromEnvironment(candidates, "JAVA_HOME");
        AddFromPath(candidates);

        return candidates
            .Where(candidate => File.Exists(candidate.ExecutablePath))
            .DistinctBy(candidate => candidate.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddFromEnvironment(List<JavaInstallation> candidates, string variable)
    {
        var home = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(home))
        {
            candidates.Add(new JavaInstallation(Path.Combine(home, "bin", "javaw.exe"), variable));
        }
    }

    private static void AddFromPath(List<JavaInstallation> candidates)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(new JavaInstallation(Path.Combine(directory.Trim('"'), "javaw.exe"), "PATH"));
        }
    }
}
