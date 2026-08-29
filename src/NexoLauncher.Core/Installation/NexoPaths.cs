namespace NexoLauncher.Core.Installation;

public interface INexoPaths
{
    string Root { get; }
    string Shared { get; }
    string Assets { get; }
    string Libraries { get; }
    string Versions { get; }
    string SharedRuntimes { get; }
    string JavaRuntimes { get; }
    string Instances { get; }
    string Cache { get; }
    string Logs { get; }
    string LauncherLogs { get; }
    string Launcher { get; }
    InstancePaths Instance(Guid id);
}

public sealed record NexoPaths(string Root) : INexoPaths
{
    public string Shared => Path.Combine(Root, "shared");
    public string Assets => Path.Combine(Shared, "assets");
    public string Libraries => Path.Combine(Shared, "libraries");
    public string Versions => Path.Combine(Shared, "versions");
    public string SharedRuntimes => Path.Combine(Shared, "runtimes");
    public string JavaRuntimes => Path.Combine(SharedRuntimes, "java");
    public string Runtime => SharedRuntimes;
    public string Instances => Path.Combine(Root, "instances");
    public string Cache => Path.Combine(Root, "cache");
    public string Logs => Path.Combine(Root, "logs");
    public string LauncherLogs => Path.Combine(Logs, "launcher");
    public string Launcher => Path.Combine(Root, "launcher");

    public string LegacyAssets => Path.Combine(Root, "assets");
    public string LegacyLibraries => Path.Combine(Root, "libraries");
    public string LegacyVersions => Path.Combine(Root, "versions");
    public string LegacyRuntime => Path.Combine(Root, "runtime");

    public static NexoPaths ForCurrentUser() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoLauncher"));

    public InstancePaths Instance(Guid id) => new(Instances, id);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Shared);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Libraries);
        Directory.CreateDirectory(Versions);
        Directory.CreateDirectory(SharedRuntimes);
        Directory.CreateDirectory(JavaRuntimes);
        Directory.CreateDirectory(Instances);
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(LauncherLogs);
        Directory.CreateDirectory(Launcher);
    }
}

public sealed record InstancePaths
{
    public InstancePaths(string instancesRoot, Guid instanceId)
    {
        if (instanceId == Guid.Empty) throw new ArgumentException("El GUID de la instancia no puede estar vacío.", nameof(instanceId));
        InstancesRoot = WithSeparator(Path.GetFullPath(instancesRoot));
        Id = instanceId;
        Root = SafeChild(InstancesRoot, instanceId.ToString("N"));
    }

    public Guid Id { get; }
    public string InstancesRoot { get; }
    public string Root { get; }
    public string Manifest => Path.Combine(Root, "instance.json");
    public string Game => Path.Combine(Root, "game");
    public string Mods => Path.Combine(Game, "mods");
    public string Config => Path.Combine(Game, "config");
    public string Saves => Path.Combine(Game, "saves");
    public string ResourcePacks => Path.Combine(Game, "resourcepacks");
    public string ShaderPacks => Path.Combine(Game, "shaderpacks");
    public string Screenshots => Path.Combine(Game, "screenshots");
    public string GameLogs => Path.Combine(Game, "logs");
    public string CrashReports => Path.Combine(Game, "crash-reports");
    public string Runtime => Path.Combine(Root, "runtime");
    public string Natives => Path.Combine(Runtime, "natives");
    public string Backups => Path.Combine(Root, "backups");

    public string NativesLaunch(Guid launchId)
    {
        if (launchId == Guid.Empty) throw new ArgumentException("El GUID del lanzamiento no puede estar vacío.", nameof(launchId));
        return SafeChild(WithSeparator(Natives), launchId.ToString("N"));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        foreach (var directory in new[]
                 {
                     Game, Mods, Config, Saves, ResourcePacks, ShaderPacks, Screenshots, GameLogs, CrashReports,
                     Runtime, Natives, Backups
                 })
            Directory.CreateDirectory(directory);
    }

    private static string SafeChild(string parent, string name)
    {
        var candidate = Path.GetFullPath(Path.Combine(parent, name));
        if (!candidate.StartsWith(WithSeparator(parent), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La ruta calculada sale del directorio autorizado de NEXO.");
        return candidate;
    }

    private static string WithSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
