using System.Xml.Linq;

namespace NexoLauncher.Minecraft.Loaders;

public static class MavenMetadataParser
{
    public static IReadOnlyList<string> ParseVersions(ReadOnlyMemory<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        var document = XDocument.Load(stream, LoadOptions.None);
        return document.Descendants("version")
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
