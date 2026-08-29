using System.IO.Compression;
using System.Text.Json;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Content;

public sealed record ModpackDescriptor(string Name, string MinecraftVersion, LoaderType Loader, string? LoaderVersion, string Format);

public static class ModpackDescriptorReader
{
    public static ModpackDescriptor Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.GetEntry("manifest.json") is { } curseForge) return ReadCurseForge(curseForge, path);
        if (archive.GetEntry("modrinth.index.json") is { } modrinth) return ReadModrinth(modrinth, path);
        if (archive.GetEntry("metadata.json") is { } nexo) return ReadMetadata(nexo, path);
        throw new InvalidDataException("El archivo no contiene un manifiesto compatible de CurseForge, Modrinth o NEXO.");
    }

    private static ModpackDescriptor ReadCurseForge(ZipArchiveEntry entry, string path)
    {
        using var document = ReadJson(entry);
        var root = document.RootElement;
        var minecraft = root.GetProperty("minecraft");
        var version = RequiredString(minecraft, "version");
        var loaders = minecraft.GetProperty("modLoaders").EnumerateArray().ToArray();
        var selected = loaders.FirstOrDefault(value => value.TryGetProperty("primary", out var primary) && primary.GetBoolean());
        if (selected.ValueKind == JsonValueKind.Undefined) selected = loaders.FirstOrDefault();
        if (selected.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("El modpack no declara un loader.");
        var loaderId = RequiredString(selected, "id");
        var separator = loaderId.IndexOf('-');
        var loaderName = separator < 0 ? loaderId : loaderId[..separator];
        var loaderVersion = separator < 0 ? null : loaderId[(separator + 1)..];
        var name = OptionalString(root, "name") ?? Path.GetFileNameWithoutExtension(path);
        return new(name, version, ParseLoader(loaderName), loaderVersion, "CurseForge");
    }

    private static ModpackDescriptor ReadModrinth(ZipArchiveEntry entry, string path)
    {
        using var document = ReadJson(entry);
        var root = document.RootElement;
        var dependencies = root.GetProperty("dependencies");
        var minecraft = RequiredString(dependencies, "minecraft");
        foreach (var (property, loader) in new[] { ("fabric-loader", LoaderType.Fabric), ("neoforge", LoaderType.NeoForge), ("forge", LoaderType.Forge), ("quilt-loader", LoaderType.Quilt) })
            if (dependencies.TryGetProperty(property, out var version) && version.ValueKind == JsonValueKind.String)
                return new(OptionalString(root, "name") ?? Path.GetFileNameWithoutExtension(path), minecraft, loader, version.GetString(), "Modrinth");
        return new(OptionalString(root, "name") ?? Path.GetFileNameWithoutExtension(path), minecraft, LoaderType.Vanilla, null, "Modrinth");
    }

    private static ModpackDescriptor ReadMetadata(ZipArchiveEntry entry, string path)
    {
        using var document = ReadJson(entry);
        var root = document.RootElement;
        var version = RequiredString(root, "gameVersion");
        var loaderName = root.TryGetProperty("loaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array
            ? loaders.EnumerateArray().FirstOrDefault().GetString()
            : null;
        var loader = string.IsNullOrWhiteSpace(loaderName) ? LoaderType.Vanilla : ParseLoader(loaderName);
        var loaderVersion = OptionalString(root, "loaderVersion");
        return new(OptionalString(root, "name") ?? Path.GetFileNameWithoutExtension(path), version, loader, loaderVersion, "NEXO Pack");
    }

    private static JsonDocument ReadJson(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return JsonDocument.Parse(stream);
    }

    private static LoaderType ParseLoader(string value) => value.Trim().ToLowerInvariant() switch
    {
        "fabric" => LoaderType.Fabric,
        "forge" => LoaderType.Forge,
        "neoforge" => LoaderType.NeoForge,
        "quilt" => LoaderType.Quilt,
        "vanilla" => LoaderType.Vanilla,
        _ => throw new NotSupportedException($"El loader '{value}' del modpack todavía no es compatible con NEXO.")
    };

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) ?? throw new InvalidDataException($"El manifiesto no declara '{property}'.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;
}
