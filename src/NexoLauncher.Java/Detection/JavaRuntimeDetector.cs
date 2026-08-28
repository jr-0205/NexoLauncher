namespace NexoLauncher.Java.Detection;

public sealed class JavaRuntimeDetector(JavaRuntimeInspector inspector)
{
    public async Task<IReadOnlyList<JavaRuntime>> DetectAsync(CancellationToken token = default)
    {
        var candidates = CandidatePaths()
            .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var runtimes = new List<JavaRuntime>(candidates.Length);
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var runtime = await inspector.InspectAsync(candidate.Path, candidate.Source, token);
                if (runtime is not null) runtimes.Add(runtime);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        return runtimes
            .DistinctBy(runtime => runtime.JavaExecutable, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(runtime => runtime.MajorVersion)
            .ThenBy(runtime => runtime.Vendor, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Path, string Source)> CandidatePaths()
    {
        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(home))
            yield return (Path.Combine(home, "bin", "java.exe"), "JAVA_HOME");

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return (Path.Combine(directory.Trim('"'), "java.exe"), "PATH");
        }

        foreach (var root in ProgramFilesRoots())
        {
            if (!Directory.Exists(root)) continue;

            var directJava = Path.Combine(root, "bin", "java.exe");
            if (File.Exists(directJava)) yield return (directJava, "Program Files");

            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(root).ToArray(); }
            catch { continue; }

            foreach (var child in children)
            {
                var candidate = Path.Combine(child, "bin", "java.exe");
                if (File.Exists(candidate)) yield return (candidate, "Program Files");
            }
        }
    }

    private static IEnumerable<string> ProgramFilesRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles)) yield break;

        yield return Path.Combine(programFiles, "Java");
        yield return Path.Combine(programFiles, "Eclipse Adoptium");
        yield return Path.Combine(programFiles, "Microsoft");
        yield return Path.Combine(programFiles, "Zulu");
    }
}
