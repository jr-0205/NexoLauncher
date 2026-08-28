using System.IO.Compression;

namespace NexoLauncher.Minecraft.Security;

public static class SafeArchiveExtractor
{
    public static void ExtractZip(string archivePath, string destination, Func<ZipArchiveEntry, bool>? include = null)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || include is not null && !include(entry)) continue;
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Entrada ZIP fuera de la ruta permitida: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }
}
