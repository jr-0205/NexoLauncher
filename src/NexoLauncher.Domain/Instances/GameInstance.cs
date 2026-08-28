namespace NexoLauncher.Domain.Instances;

public readonly record struct InstanceId(Guid Value)
{
    public static InstanceId New() => new(Guid.NewGuid());
    public static InstanceId Parse(string value) => new(Guid.ParseExact(value, "N"));
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
    public required string DirectoryName { get; init; }
    public InstanceSettings Settings { get; init; } = new();
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    public static GameInstance Create(string name, string minecraftVersion, LoaderType loader = LoaderType.Vanilla, string? loaderVersion = null)
    {
        name = name?.Trim() ?? string.Empty;
        minecraftVersion = minecraftVersion?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 64) throw new ArgumentException("El nombre debe tener entre 1 y 64 caracteres.", nameof(name));
        if (string.IsNullOrWhiteSpace(minecraftVersion)) throw new ArgumentException("La versión de Minecraft es obligatoria.", nameof(minecraftVersion));
        if (loader != LoaderType.Vanilla && string.IsNullOrWhiteSpace(loaderVersion)) throw new ArgumentException("La versión del loader es obligatoria.", nameof(loaderVersion));
        var id = InstanceId.New();
        var now = DateTimeOffset.UtcNow;
        return new GameInstance { Id = id, Name = name, MinecraftVersion = minecraftVersion, Loader = loader, LoaderVersion = loaderVersion, DirectoryName = id.ToString(), CreatedAt = now, UpdatedAt = now };
    }
}
