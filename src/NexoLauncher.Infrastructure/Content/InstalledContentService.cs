namespace NexoLauncher.Infrastructure.Content;

public sealed record InstalledContentEntry(
    string Category,
    string Name,
    string RelativePath,
    long SizeBytes,
    bool Enabled,
    bool CanToggle,
    bool IsDirectory);

public sealed class InstalledContentService
{
    private static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mods"] = "MODS",
        ["resourcepacks"] = "TEXTURAS",
        ["shaderpacks"] = "SHADERS",
        ["datapacks"] = "DATAPACKS",
        ["config"] = "CONFIGURACIÓN"
    };

    public IReadOnlyList<InstalledContentEntry> List(string gameDirectory)
    {
        var root = NormalizeRoot(gameDirectory);
        var result = new List<InstalledContentEntry>();
        foreach (var pair in Categories)
        {
            var directory = Path.Combine(root, pair.Key);
            if (!Directory.Exists(directory)) continue;
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (!ShouldDisplayFile(pair.Key, info.Name)) continue;
                var disabled = pair.Key.Equals("mods", StringComparison.OrdinalIgnoreCase) && info.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                result.Add(new InstalledContentEntry(
                    pair.Value,
                    DisplayName(info.Name),
                    Path.GetRelativePath(root, file),
                    info.Length,
                    !disabled,
                    pair.Key.Equals("mods", StringComparison.OrdinalIgnoreCase) && (info.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || disabled),
                    false));
            }

            if (pair.Key is "resourcepacks" or "shaderpacks" or "datapacks")
            {
                foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    var info = new DirectoryInfo(child);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    result.Add(new InstalledContentEntry(
                        pair.Value,
                        info.Name,
                        Path.GetRelativePath(root, child),
                        0,
                        true,
                        false,
                        true));
                }
            }
        }

        return result
            .OrderBy(value => CategoryOrder(value.Category))
            .ThenBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public InstalledContentEntry Toggle(string gameDirectory, InstalledContentEntry entry)
    {
        if (!entry.CanToggle || entry.IsDirectory)
            throw new InvalidOperationException("Este elemento no puede activarse o desactivarse desde NEXO.");
        var root = NormalizeRoot(gameDirectory);
        var source = ResolveManagedPath(root, entry.RelativePath);
        if (!File.Exists(source)) throw new FileNotFoundException("El mod seleccionado ya no existe.", source);

        string destination;
        if (source.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            destination = source[..^".disabled".Length];
        else if (source.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            destination = source + ".disabled";
        else
            throw new InvalidDataException("Sólo los JAR de mods pueden activarse o desactivarse.");

        if (File.Exists(destination))
            throw new IOException("Ya existe otro archivo con el nombre de destino.");
        File.Move(source, destination);
        return entry with
        {
            RelativePath = Path.GetRelativePath(root, destination),
            Enabled = !entry.Enabled,
            Name = DisplayName(Path.GetFileName(destination))
        };
    }

    public void Delete(string gameDirectory, InstalledContentEntry entry)
    {
        var root = NormalizeRoot(gameDirectory);
        var path = ResolveManagedPath(root, entry.RelativePath);
        if (entry.IsDirectory)
        {
            if (!Directory.Exists(path)) return;
            var info = new DirectoryInfo(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("NEXO no elimina directorios enlazados o junctions.");
            EnsureTreeContainsNoReparsePoints(path);
            Directory.Delete(path, recursive: true);
        }
        else
        {
            if (!File.Exists(path)) return;
            var info = new FileInfo(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("NEXO no elimina archivos enlazados.");
            File.Delete(path);
        }
    }

    public string ResolvePath(string gameDirectory, InstalledContentEntry entry)
        => ResolveManagedPath(NormalizeRoot(gameDirectory), entry.RelativePath);

    private static bool ShouldDisplayFile(string category, string name)
    {
        if (category.Equals("mods", StringComparison.OrdinalIgnoreCase))
            return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
        if (category.Equals("config", StringComparison.OrdinalIgnoreCase))
            return new[] { ".json", ".toml", ".properties", ".cfg", ".yaml", ".yml" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
        return true;
    }

    private static string DisplayName(string fileName)
        => fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".disabled".Length]
            : fileName;

    private static int CategoryOrder(string category) => category switch
    {
        "MODS" => 0,
        "TEXTURAS" => 1,
        "SHADERS" => 2,
        "DATAPACKS" => 3,
        "CONFIGURACIÓN" => 4,
        _ => 10
    };

    private static string NormalizeRoot(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) throw new ArgumentException("La carpeta del perfil es obligatoria.", nameof(gameDirectory));
        var root = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(root);
        if (new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("La carpeta game del perfil no puede ser un enlace o junction.");
        return root;
    }

    private static string ResolveManagedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("La ruta de contenido no es válida.");
        var normalizedRelative = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var top = normalizedRelative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (top is null || !Categories.ContainsKey(top))
            throw new InvalidDataException("El elemento no pertenece a una sección administrable de contenido.");
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, normalizedRelative));
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La ruta de contenido intenta salir de la carpeta del perfil.");
        return candidate;
    }

    private static void EnsureTreeContainsNoReparsePoints(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("NEXO no elimina contenido que contiene enlaces o junctions.");
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            if (new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("NEXO no elimina contenido que contiene archivos enlazados.");
    }
}
