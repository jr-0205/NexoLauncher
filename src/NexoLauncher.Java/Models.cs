namespace NexoLauncher.Java;

public sealed record JavaRuntime(
    string JavaExecutable,
    string JavawExecutable,
    int MajorVersion,
    string FullVersion,
    string Vendor,
    string Architecture,
    string Source)
{
    public bool Is64Bit => Architecture.Contains("64", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => $"Java {MajorVersion} · {Vendor} · {Architecture}";
}

public sealed record JavaCompatibilityResult(bool IsCompatible, string Message);
