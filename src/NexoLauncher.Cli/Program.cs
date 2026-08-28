using NexoLauncher.Core.Installation;
using NexoLauncher.Core.Java;

if (args is not ["diagnose"])
{
    Console.WriteLine("Nexo Launcher core CLI");
    Console.WriteLine("Usage: nexo diagnose");
    return 0;
}

var paths = NexoPaths.ForCurrentUser();
var javaInstallations = new JavaDetector().Detect();

Console.WriteLine($"Data directory: {paths.Root}");
Console.WriteLine(javaInstallations.Count == 0
    ? "Java: not detected"
    : $"Java: {javaInstallations[0].ExecutablePath} ({javaInstallations[0].Source})");

return javaInstallations.Count == 0 ? 2 : 0;
