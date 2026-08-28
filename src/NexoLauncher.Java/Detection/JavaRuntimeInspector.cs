using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NexoLauncher.Java.Detection;

public sealed partial class JavaRuntimeInspector
{
    public async Task<JavaRuntime?> InspectAsync(string javaExecutable, string source, CancellationToken token = default)
    {
        if (!File.Exists(javaExecutable)) return null;
        var info = new ProcessStartInfo
        {
            FileName = javaExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-XshowSettings:properties");
        info.ArgumentList.Add("-version");
        using var process = Process.Start(info);
        if (process is null) return null;
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) return null;
        return Parse(await stdout + await stderr, javaExecutable, source);
    }

    public static JavaRuntime? Parse(string output, string javaExecutable, string source)
    {
        var version = Property(output, "java.version") ?? VersionPattern().Match(output).Groups["version"].Value;
        if (string.IsNullOrWhiteSpace(version)) return null;
        var major = ParseMajor(version);
        if (major <= 0) return null;
        var vendor = Property(output, "java.vendor") ?? "Proveedor desconocido";
        var architecture = Property(output, "os.arch") ?? "Arquitectura desconocida";
        var javaw = Path.Combine(Path.GetDirectoryName(javaExecutable)!, "javaw.exe");
        return new JavaRuntime(Path.GetFullPath(javaExecutable), javaw, major, version, vendor, architecture, source);
    }

    public static int ParseMajor(string version)
    {
        var normalized = version.Trim().Trim('"');
        var pieces = normalized.Split(['.', '-', '+'], StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length == 0) return 0;
        if (pieces[0] == "1" && pieces.Length > 1 && int.TryParse(pieces[1], out var legacy)) return legacy;
        return int.TryParse(pieces[0], out var modern) ? modern : 0;
    }

    private static string? Property(string output, string name)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(name + " =", StringComparison.Ordinal)) return trimmed[(trimmed.IndexOf('=') + 1)..].Trim();
        }
        return null;
    }

    [GeneratedRegex("version \\\"(?<version>[^\\\"]+)\\\"")]
    private static partial Regex VersionPattern();
}
