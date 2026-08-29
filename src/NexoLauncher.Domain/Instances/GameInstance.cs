namespace NexoLauncher.Domain.Instances;

public readonly record struct InstanceId(Guid Value)
{
    public static InstanceId New() => new(Guid.NewGuid());

    public static InstanceId Parse(string value)
    {
        if (Guid.TryParseExact(value, "N", out var compact)) return new InstanceId(compact);
        if (Guid.TryParse(value, out var guid)) return new InstanceId(guid);
        throw new FormatException("El identificador de instancia no es un GUID válido.");
    }

    public override string ToString() => Value.ToString("N");
}

public enum LoaderType
{
    Vanilla,
    Fabric,
    Forge,
    NeoForge,
    Quilt
}

public sealed record InstanceSettings(
    int? MemoryMiB = null,
    string? JavaPath = null,
    IReadOnlyList<string>? JvmArguments = null,
    int? WindowWidth = null,
    int? WindowHeight = null,
    bool? Fullscreen = null);

public sealed record GameInstance
{
    public required InstanceId Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? IconPath { get; init; }
    public required string MinecraftVersion { get; init; }
    public LoaderType Loader { get; init; } = LoaderType.Vanilla;
    public string? LoaderVersion { get; init; }

    // Compatibilidad de API: ya no deriva del nombre visible. Siempre es el GUID físico.
    public required string DirectoryName { get; init; }
    public InstanceSettings Settings { get; init; } = new();
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastPlayedAt { get; init; }

    public static GameInstance Create(string name, string minecraftVersion, LoaderType loader = LoaderType.Vanilla, string? loaderVersion = null)
    {
        name = name?.Trim() ?? string.Empty;
        minecraftVersion = minecraftVersion?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 64) throw new ArgumentException("El nombre debe tener entre 1 y 64 caracteres.", nameof(name));
        if (string.IsNullOrWhiteSpace(minecraftVersion)) throw new ArgumentException("La versión de Minecraft es obligatoria.", nameof(minecraftVersion));
        if (loader != LoaderType.Vanilla && string.IsNullOrWhiteSpace(loaderVersion)) throw new ArgumentException("La versión del loader es obligatoria.", nameof(loaderVersion));
        if (loader == LoaderType.Vanilla) loaderVersion = null;

        var id = InstanceId.New();
        var now = DateTimeOffset.UtcNow;
        return new GameInstance
        {
            Id = id,
            Name = name,
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loaderVersion,
            DirectoryName = id.ToString(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}

// Se conserva únicamente para reconocer nombres de layouts históricos durante migraciones.
public static class InstanceDirectoryName
{
    public static string Create(LoaderType loader, string profileName) =>
        Path.Combine(loader.ToString(), Sanitize(profileName));

    public static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var characters = value.Trim()
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '-' : character)
            .ToArray();
        var name = new string(characters).Trim().TrimEnd('.');
        while (name.Contains("  ", StringComparison.Ordinal)) name = name.Replace("  ", " ", StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(name)) name = "Perfil";
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(name, StringComparer.OrdinalIgnoreCase)) name += " - Perfil";
        return name.Length <= 64 ? name : name[..64].TrimEnd();
    }
}
