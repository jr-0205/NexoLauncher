namespace NexoLauncher.Minecraft.Loaders;

public static class FabricLibraryResolver
{
    public static (string RelativePath, string FileName) Resolve(string coordinate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(coordinate);
        var parts = coordinate.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 3 or > 4 || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Coordenada Maven de Fabric no válida: " + coordinate);

        if (parts.Any(Unsafe) || parts[0].Split('.').Any(segment => segment.Length == 0))
            throw new InvalidDataException("Coordenada Maven de Fabric no segura: " + coordinate);

        var groupPath = parts[0].Replace('.', '/');
        var classifier = parts.Length == 4 ? "-" + parts[3] : string.Empty;
        var fileName = $"{parts[1]}-{parts[2]}{classifier}.jar";
        return ($"{groupPath}/{parts[1]}/{parts[2]}/{fileName}", fileName);
    }

    private static bool Unsafe(string value)
        => value is "." or ".."
           || value.Contains('/')
           || value.Contains('\\')
           || value.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-' and not '+');
}
