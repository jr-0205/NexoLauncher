using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexoLauncher.Infrastructure.Content;

public sealed record InstalledContentEntry(
    string Category,
    string Name,
    string RelativePath,
    long SizeBytes,
    bool Enabled,
    bool CanToggle,
    bool IsDirectory,
    string? IconDataUrl = null);

public sealed class InstalledContentService
{
    private const int MaxIconBytes = 512 * 1024;
    private const int MaxMetadataBytes = 512 * 1024;

    private static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["mods"] = "MODS",
        ["resourcepacks"] = "TEXTURAS",
        ["shaderpacks"] = "SHADERS",
        ["datapacks"] = "DATAPACKS",
        ["config"] = "CONFIGURACIÓN"
    };

    private readonly Dictionary<string, IconCacheEntry> iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object iconCacheGate = new();

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

                var disabled = pair.Key.Equals("mods", StringComparison.OrdinalIgnoreCase) &&
                               info.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                result.Add(new InstalledContentEntry(
                    pair.Value,
                    DisplayName(info.Name),
                    Path.GetRelativePath(root, file),
                    info.Length,
                    !disabled,
                    pair.Key.Equals("mods", StringComparison.OrdinalIgnoreCase) &&
                    (info.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || disabled),
                    false,
                    ResolveFileIcon(file, pair.Key)));
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
                        true,
                        ResolveDirectoryIcon(child)));
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
        lock (iconCacheGate)
        {
            iconCache.Remove(source);
            iconCache.Remove(destination);
        }

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
        lock (iconCacheGate) iconCache.Remove(path);

        if (entry.IsDirectory)
        {
            if (!Directory.Exists(path)) return;
            EnsureDirectoryTreeIsPhysical(path);
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

    private string? ResolveFileIcon(string path, string category)
    {
        if (category.Equals("config", StringComparison.OrdinalIgnoreCase)) return null;

        var info = new FileInfo(path);
        var stamp = new IconStamp(info.LastWriteTimeUtc.Ticks, info.Length);
        lock (iconCacheGate)
        {
            if (iconCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                return cached.DataUrl;
        }

        string? dataUrl = null;
        try
        {
            dataUrl = category.Equals("mods", StringComparison.OrdinalIgnoreCase)
                ? ReadModIcon(path)
                : ReadPackArchiveIcon(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            dataUrl = null;
        }

        lock (iconCacheGate) iconCache[path] = new IconCacheEntry(stamp, dataUrl);
        return dataUrl;
    }

    private string? ResolveDirectoryIcon(string directory)
    {
        var icon = Path.Combine(directory, "pack.png");
        if (!File.Exists(icon)) return null;
        var info = new FileInfo(icon);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;

        var stamp = new IconStamp(info.LastWriteTimeUtc.Ticks, info.Length);
        lock (iconCacheGate)
        {
            if (iconCache.TryGetValue(icon, out var cached) && cached.Stamp == stamp)
                return cached.DataUrl;
        }

        string? dataUrl = null;
        try
        {
            if (info.Length is > 0 and <= MaxIconBytes)
                dataUrl = ToDataUrl(File.ReadAllBytes(icon), ".png");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            dataUrl = null;
        }

        lock (iconCacheGate) iconCache[icon] = new IconCacheEntry(stamp, dataUrl);
        return dataUrl;
    }

    private static string? ReadModIcon(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var iconPath = ReadFabricIconPath(archive)
                       ?? ReadQuiltIconPath(archive)
                       ?? ReadForgeIconPath(archive);

        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            var declared = FindEntry(archive, iconPath);
            var data = declared is null ? null : ReadImageEntry(declared);
            if (data is not null) return data;
        }

        foreach (var fallback in new[] { "icon.png", "logo.png", "pack.png" })
        {
            var entry = FindEntry(archive, fallback);
            var data = entry is null ? null : ReadImageEntry(entry);
            if (data is not null) return data;
        }

        return null;
    }

    private static string? ReadPackArchiveIcon(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = FindEntry(archive, "pack.png");
        return entry is null ? null : ReadImageEntry(entry);
    }

    private static string? ReadFabricIconPath(ZipArchive archive)
    {
        var entry = FindEntry(archive, "fabric.mod.json");
        if (entry is null || entry.Length <= 0 || entry.Length > MaxMetadataBytes) return null;
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.TryGetProperty("icon", out var icon) ? ReadJsonIcon(icon) : null;
    }

    private static string? ReadQuiltIconPath(ZipArchive archive)
    {
        var entry = FindEntry(archive, "quilt.mod.json");
        if (entry is null || entry.Length <= 0 || entry.Length > MaxMetadataBytes) return null;
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("quilt_loader", out var loader) ||
            !loader.TryGetProperty("metadata", out var metadata) ||
            !metadata.TryGetProperty("icon", out var icon))
            return null;
        return ReadJsonIcon(icon);
    }

    private static string? ReadJsonIcon(JsonElement icon)
    {
        if (icon.ValueKind == JsonValueKind.String) return NormalizeArchivePath(icon.GetString());
        if (icon.ValueKind != JsonValueKind.Object) return null;

        return icon.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .OrderByDescending(property => int.TryParse(property.Name, out var size) ? size : 0)
            .Select(property => NormalizeArchivePath(property.Value.GetString()))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ReadForgeIconPath(ZipArchive archive)
    {
        foreach (var metadataPath in new[] { "META-INF/neoforge.mods.toml", "META-INF/mods.toml" })
        {
            var entry = FindEntry(archive, metadataPath);
            if (entry is null || entry.Length <= 0 || entry.Length > MaxMetadataBytes) continue;
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            var match = Regex.Match(text, "(?im)^\\s*logoFile\\s*=\\s*[\\\"'](?<path>[^\\\"']+)[\\\"']");
            if (match.Success) return NormalizeArchivePath(match.Groups["path"].Value);
        }
        return null;
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string path)
    {
        var normalized = NormalizeArchivePath(path);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        return archive.Entries.FirstOrDefault(entry =>
            string.Equals(entry.FullName.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeArchivePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part == "..")) return null;
        return normalized;
    }

    private static string? ReadImageEntry(ZipArchiveEntry entry)
    {
        if (entry.Length is <= 0 or > MaxIconBytes) return null;
        var extension = Path.GetExtension(entry.FullName);
        if (MimeType(extension) is null) return null;

        using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        input.CopyTo(output);
        if (output.Length > MaxIconBytes) return null;
        return ToDataUrl(output.ToArray(), extension);
    }

    private static string? ToDataUrl(byte[] bytes, string extension)
    {
        var mime = MimeType(extension);
        return mime is null || bytes.Length == 0 || bytes.Length > MaxIconBytes
            ? null
            : $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string? MimeType(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => null
    };

    private static bool ShouldDisplayFile(string category, string name)
    {
        if (category.Equals("mods", StringComparison.OrdinalIgnoreCase))
            return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
        if (category.Equals("config", StringComparison.OrdinalIgnoreCase))
            return new[] { ".json", ".toml", ".properties", ".cfg", ".yaml", ".yml" }
                .Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
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
        if (string.IsNullOrWhiteSpace(gameDirectory))
            throw new ArgumentException("La carpeta del perfil es obligatoria.", nameof(gameDirectory));
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

    private static void EnsureDirectoryTreeIsPhysical(string root)
    {
        var info = new DirectoryInfo(root);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("NEXO no elimina directorios enlazados o junctions.");

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
            if (new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("NEXO no elimina contenido que contiene archivos enlazados.");

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
            EnsureDirectoryTreeIsPhysical(directory);
    }

    private readonly record struct IconStamp(long LastWriteTicks, long Length);
    private sealed record IconCacheEntry(IconStamp Stamp, string? DataUrl);
}
