using NexoLauncher.Core.Installation;

namespace NexoLauncher.Minecraft.Installation;

public sealed class MinecraftPaths
{
    private readonly NexoPaths layout;

    public MinecraftPaths(string dataRoot, string? cacheRoot = null, string? logsRoot = null)
    {
        DataRoot = Path.GetFullPath(dataRoot);
        layout = new NexoPaths(DataRoot);
        Cache = Path.GetFullPath(cacheRoot ?? layout.Cache);
        Logs = Path.GetFullPath(logsRoot ?? layout.LauncherLogs);
    }

    public string DataRoot { get; }
    public string Root => layout.Shared;
    public string Shared => layout.Shared;
    public string Cache { get; }
    public string Logs { get; }
    public string Versions => layout.Versions;
    public string Libraries => layout.Libraries;
    public string Assets => layout.Assets;
    public string Runtimes => layout.SharedRuntimes;
    public string VersionDirectory(string id) => SafeVersionDirectory(id);
    public string VersionJson(string id) => Path.Combine(VersionDirectory(id), id + ".json");
    public string ClientJar(string id) => Path.Combine(VersionDirectory(id), id + ".jar");

    // Compatibilidad para llamadas antiguas sin instancia. Los lanzamientos normales usan
    // el gameDirectory privado de la instancia y natives efímeros junto a ese perfil.
    public string GameDirectory(string id) => Path.Combine(layout.Launcher, "legacy-game", SafeSegment(id));
    public string Natives(string id) => Path.Combine(layout.Launcher, "runtime", "natives", SafeSegment(id));

    public string FabricProfile(string minecraftVersion, string loaderVersion) =>
        Path.Combine(VersionDirectory(minecraftVersion), "loaders", "fabric", SafeSegment(loaderVersion), "profile.json");
    public string LoaderProfile(string loaderId, string minecraftVersion, string loaderVersion) =>
        Path.Combine(VersionDirectory(minecraftVersion), "loaders", SafeSegment(loaderId), SafeSegment(loaderVersion), "profile.json");
    public string LoaderInstaller(string loaderId, string minecraftVersion, string loaderVersion) =>
        Path.Combine(VersionDirectory(minecraftVersion), "loaders", SafeSegment(loaderId), SafeSegment(loaderVersion), "installer.jar");

    public void EnsureCreated()
    {
        layout.EnsureCreated();
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Logs);
    }

    private string SafeVersionDirectory(string id)
    {
        var directory = Path.GetFullPath(Path.Combine(Versions, SafeSegment(id)));
        if (!directory.StartsWith(WithSeparator(Versions), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Identificador de versión fuera de shared/versions.");
        return directory;
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Segmento de ruta vacío.");
        if (Path.IsPathRooted(value) || value is "." or ".." || value.Contains('/') || value.Contains('\\'))
            throw new InvalidDataException("Segmento de ruta no válido.");
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Segmento de ruta no válido.");
        return value;
    }

    private static string WithSeparator(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
