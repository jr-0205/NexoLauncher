namespace NexoLauncher.Java.Detection;

public sealed class JavaRuntimeDetector(JavaRuntimeInspector inspector)
{
    public async Task<IReadOnlyList<JavaRuntime>> DetectAsync(CancellationToken token = default)
    {
        var candidates = CandidatePaths().DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var runtimes = new List<JavaRuntime>();
        foreach (var candidate in candidates)
        {
            try
            {
                var runtime = await inspector.InspectAsync(candidate.Path, candidate.Source, token);
                if (runtime is not null) runtimes.Add(runtime);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        return runtimes.DistinctBy(runtime => runtime.JavaExecutable, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(runtime => runtime.MajorVersion).ToArray();
    }

    private static IEnumerable<(string Path, string Source)> CandidatePaths()
    {
        var home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(home)) yield return (Path.Combine(home, "bin", "java.exe"), "JAVA_HOME");
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return (Path.Combine(directory.Trim('"'), "java.exe"), "PATH");

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu")
        };
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerator<string>? enumerator = null;
            try { enumerator = Directory.EnumerateFiles(root, "java.exe", SearchOption.AllDirectories).GetEnumerator(); }
            catch { }
            if (enumerator is null) continue;
            using (enumerator)
            {
                while (true)
                {
                    try { if (!enumerator.MoveNext()) break; }
                    catch { break; }
                    yield return (enumerator.Current, "Program Files");
                }
            }
        }
    }
}
