using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using NexoLauncher.Core.Installation;
using NexoLauncher.Domain.Instances;

namespace NexoLauncher.Infrastructure.Content;

public sealed record NexoInGameArtifactDependency(
    string Source,
    string ProjectId,
    string ProjectType,
    string? DetectPattern = null);

public sealed record NexoInGameArtifact(
    string MinecraftVersion,
    string Loader,
    string NexoInGameVersion,
    string Status,
    string FileName,
    string? RelativePath,
    string? DownloadUrl,
    string Sha256,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<NexoInGameArtifactDependency>? Dependencies = null);

public sealed record NexoInGameArtifactCatalog(
    int SchemaVersion,
    IReadOnlyList<NexoInGameArtifact> Artifacts);

public sealed record NexoInGameInstallResult(
    string Version,
    string FileName,
    bool UsedCache,
    IReadOnlyList<string> DependenciesInstalled);

/// <summary>
/// Instala NEXO In-Game desde artefactos ya compilados. El usuario final nunca
/// necesita Gradle ni un JDK de desarrollo. La build exacta se resuelve por
/// Minecraft + loader, se valida con SHA-256, se cachea una sola vez y después
/// se instala en el mods/ privado de cada instancia.
/// </summary>
public sealed class NexoInGameArtifactService
{
    public const int CatalogSchema = 1;
    private const int InstallManifestSchema = 1;
    private const string InstallManifestName = "nexo-ingame.json";
    private static readonly Uri RemoteCatalogUri = new(
        "https://raw.githubusercontent.com/jr-0205/NexoLauncher/main/artifacts/nexo-ingame/catalog.json");
    private static readonly Uri RemoteArtifactBaseUri = new(
        "https://raw.githubusercontent.com/jr-0205/NexoLauncher/main/artifacts/nexo-ingame/");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient http;
    private readonly ModrinthContentClient modrinth;
    private readonly NexoPaths paths;
    private readonly string? developmentArtifactRoot;

    public NexoInGameArtifactService(
        HttpClient http,
        ModrinthContentClient modrinth,
        NexoPaths paths,
        string? developmentArtifactRoot = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.modrinth = modrinth ?? throw new ArgumentNullException(nameof(modrinth));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.developmentArtifactRoot = string.IsNullOrWhiteSpace(developmentArtifactRoot)
            ? null
            : Path.GetFullPath(developmentArtifactRoot);
    }

    public async Task<NexoInGameArtifactCatalog> LoadCatalogAsync(CancellationToken token = default)
    {
        if (developmentArtifactRoot is not null)
        {
            var localCatalog = Path.Combine(developmentArtifactRoot, "catalog.json");
            if (File.Exists(localCatalog))
            {
                await using var stream = File.OpenRead(localCatalog);
                var local = await JsonSerializer.DeserializeAsync<NexoInGameArtifactCatalog>(stream, Json, token)
                            ?? throw new InvalidDataException("El catalogo local de NEXO In-Game esta vacio.");
                ValidateCatalog(local);
                return local;
            }
        }

        using var request = CreateRequest(RemoteCatalogUri);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"No se pudo obtener el catalogo publicado de NEXO In-Game ({(int)response.StatusCode}).");
        var remote = await response.Content.ReadFromJsonAsync<NexoInGameArtifactCatalog>(Json, token)
                     ?? throw new InvalidDataException("El catalogo publicado de NEXO In-Game esta vacio.");
        ValidateCatalog(remote);
        return remote;
    }

    public async Task<NexoInGameArtifact?> FindPublishedArtifactAsync(GameInstance instance, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return SelectPublishedArtifact(await LoadCatalogAsync(token), instance);
    }

    public static NexoInGameArtifact? SelectPublishedArtifact(NexoInGameArtifactCatalog catalog, GameInstance instance)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(instance);
        var loader = instance.Loader.ToString();
        return catalog.Artifacts
            .Where(artifact => string.Equals(artifact.Status, "published", StringComparison.OrdinalIgnoreCase))
            .Where(artifact => string.Equals(artifact.MinecraftVersion, instance.MinecraftVersion, StringComparison.Ordinal))
            .Where(artifact => string.Equals(artifact.Loader, loader, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(artifact => artifact.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    public async Task<NexoInGameInstallResult> InstallAsync(
        GameInstance instance,
        string gameDirectory,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        gameDirectory = Path.GetFullPath(gameDirectory);
        var catalog = await LoadCatalogAsync(token);
        var artifact = SelectPublishedArtifact(catalog, instance);
        if (artifact is null)
        {
            var planned = catalog.Artifacts.FirstOrDefault(candidate =>
                string.Equals(candidate.MinecraftVersion, instance.MinecraftVersion, StringComparison.Ordinal) &&
                string.Equals(candidate.Loader, instance.Loader.ToString(), StringComparison.OrdinalIgnoreCase));
            var detail = planned is null
                ? $"No existe una build de NEXO In-Game para Minecraft {instance.MinecraftVersion} + {instance.Loader}."
                : $"NEXO In-Game {planned.NexoInGameVersion} para Minecraft {instance.MinecraftVersion} + {instance.Loader} esta registrado, pero su JAR aun no se ha publicado.";
            throw new NotSupportedException(detail);
        }

        ValidatePublishedArtifact(artifact);
        progress?.Report($"Resolviendo NEXO In-Game {artifact.NexoInGameVersion}...");
        var (cachedArtifact, usedCache) = await EnsureCachedAsync(artifact, progress, token);

        progress?.Report("Instalando NEXO In-Game...");
        var installedName = await InstallArtifactAsync(cachedArtifact, artifact, instance, gameDirectory, token);

        var dependencyFiles = new List<string>();
        foreach (var dependency in artifact.Dependencies ?? [])
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(dependency.Source, "modrinth", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Fuente de dependencia no soportada: {dependency.Source}");

            var targetFolder = dependency.ProjectType switch
            {
                "resourcepack" => Path.Combine(gameDirectory, "resourcepacks"),
                "shader" => Path.Combine(gameDirectory, "shaderpacks"),
                "datapack" => Path.Combine(gameDirectory, "datapacks"),
                _ => Path.Combine(gameDirectory, "mods")
            };
            if (!string.IsNullOrWhiteSpace(dependency.DetectPattern) && Directory.Exists(targetFolder) &&
                Directory.EnumerateFiles(targetFolder, dependency.DetectPattern, SearchOption.TopDirectoryOnly).Any())
                continue;

            progress?.Report($"Instalando dependencia {dependency.ProjectId}...");
            var project = new ContentCatalogProject(
                dependency.ProjectId,
                dependency.ProjectId,
                string.Empty,
                "NEXO",
                dependency.ProjectType,
                null,
                0);
            var installed = await modrinth.InstallAsync(
                project,
                instance.MinecraftVersion,
                instance.Loader.ToString().ToLowerInvariant(),
                gameDirectory,
                token);
            dependencyFiles.AddRange(installed.FileNames);
        }

        progress?.Report("NEXO In-Game listo. Right Shift habilitado.");
        return new NexoInGameInstallResult(
            artifact.NexoInGameVersion,
            installedName,
            usedCache,
            dependencyFiles);
    }

    private async Task<(string Path, bool UsedCache)> EnsureCachedAsync(
        NexoInGameArtifact artifact,
        IProgress<string>? progress,
        CancellationToken token)
    {
        var cacheDirectory = Path.Combine(
            paths.Cache,
            "nexo-ingame",
            SafeSegment(artifact.NexoInGameVersion),
            SafeSegment(artifact.Loader.ToLowerInvariant()),
            SafeSegment(artifact.MinecraftVersion));
        Directory.CreateDirectory(cacheDirectory);
        var destination = SafeChild(cacheDirectory, artifact.FileName);

        if (File.Exists(destination) && await HasSha256Async(destination, artifact.Sha256, token))
        {
            progress?.Report("Usando NEXO In-Game desde cache verificada...");
            return (destination, true);
        }
        if (File.Exists(destination)) File.Delete(destination);

        var temporary = destination + ".download";
        if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            var local = ResolveDevelopmentArtifact(artifact);
            if (local is not null)
            {
                progress?.Report("Copiando artefacto precompilado local...");
                await using var source = File.OpenRead(local);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(output, token);
            }
            else
            {
                var uri = ResolveRemoteArtifactUri(artifact);
                progress?.Report("Descargando artefacto precompilado...");
                using var request = CreateRequest(uri);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await source.CopyToAsync(output, token);
            }

            progress?.Report("Verificando SHA-256 de NEXO In-Game...");
            if (!await HasSha256Async(temporary, artifact.Sha256, token))
                throw new InvalidDataException("El JAR de NEXO In-Game no supero la verificacion SHA-256.");
            File.Move(temporary, destination, true);
            return (destination, false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private async Task<string> InstallArtifactAsync(
        string cachedArtifact,
        NexoInGameArtifact artifact,
        GameInstance instance,
        string gameDirectory,
        CancellationToken token)
    {
        var mods = Path.Combine(gameDirectory, "mods");
        EnsurePhysicalDirectory(gameDirectory, "gameDirectory");
        if (Directory.Exists(mods)) EnsurePhysicalDirectory(mods, "mods/");
        Directory.CreateDirectory(mods);

        var destination = SafeChild(mods, artifact.FileName);
        var temporary = destination + ".nexo-install";
        if (File.Exists(temporary)) File.Delete(temporary);
        try
        {
            await using (var source = File.OpenRead(cachedArtifact))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(output, token);
            if (!await HasSha256Async(temporary, artifact.Sha256, token))
                throw new InvalidDataException("La copia local de NEXO In-Game no supero SHA-256.");
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        foreach (var old in Directory.EnumerateFiles(mods, "nexo-ingame*.jar", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) continue;
            var info = new FileInfo(old);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            File.Delete(old);
        }

        var runtime = Path.Combine(Directory.GetParent(gameDirectory)?.FullName
                                   ?? throw new InvalidOperationException("No se pudo resolver la raiz de la instancia."), "runtime");
        if (Directory.Exists(runtime)) EnsurePhysicalDirectory(runtime, "runtime/");
        Directory.CreateDirectory(runtime);
        var manifestPath = Path.Combine(runtime, InstallManifestName);
        var manifest = new InstallManifest(
            InstallManifestSchema,
            artifact.NexoInGameVersion,
            instance.MinecraftVersion,
            instance.Loader.ToString(),
            artifact.FileName,
            artifact.Sha256.ToLowerInvariant(),
            DateTimeOffset.UtcNow);
        await WriteJsonAtomicAsync(manifestPath, manifest, token);
        return artifact.FileName;
    }

    private string? ResolveDevelopmentArtifact(NexoInGameArtifact artifact)
    {
        if (developmentArtifactRoot is null || string.IsNullOrWhiteSpace(artifact.RelativePath)) return null;
        var candidate = SafeChild(developmentArtifactRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(candidate) ? candidate : null;
    }

    private static Uri ResolveRemoteArtifactUri(NexoInGameArtifact artifact)
    {
        if (!string.IsNullOrWhiteSpace(artifact.DownloadUrl))
        {
            if (!Uri.TryCreate(artifact.DownloadUrl, UriKind.Absolute, out var explicitUri) || explicitUri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("downloadUrl de NEXO In-Game debe ser HTTPS absoluto.");
            return explicitUri;
        }
        if (string.IsNullOrWhiteSpace(artifact.RelativePath))
            throw new InvalidDataException("El artefacto publicado no define relativePath ni downloadUrl.");
        return new Uri(RemoteArtifactBaseUri, artifact.RelativePath.Replace('\\', '/'));
    }

    private static void ValidateCatalog(NexoInGameArtifactCatalog catalog)
    {
        if (catalog.SchemaVersion != CatalogSchema)
            throw new InvalidDataException($"Catalogo NEXO In-Game schema {catalog.SchemaVersion} no soportado.");
        if (catalog.Artifacts is null) throw new InvalidDataException("El catalogo no contiene artifacts.");
    }

    private static void ValidatePublishedArtifact(NexoInGameArtifact artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.FileName) || artifact.FileName != Path.GetFileName(artifact.FileName) ||
            !artifact.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Nombre de JAR invalido en el catalogo NEXO In-Game.");
        if (artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("El artefacto publicado no contiene un SHA-256 valido.");
    }

    private static async Task<bool> HasSha256Async(string path, string expected, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("NexoLauncher/0.5.2 (github.com/jr-0205/NexoLauncher)");
        return request;
    }

    private static async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Json), token);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void EnsurePhysicalDirectory(string path, string name)
    {
        if (!Directory.Exists(path)) return;
        if (new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException($"{name} no puede ser un enlace o junction.");
    }

    private static string SafeChild(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La ruta del artefacto sale del directorio autorizado.");
        return candidate;
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Segmento de ruta invalido en catalogo NEXO In-Game.");
        return value;
    }

    private sealed record InstallManifest(
        int SchemaVersion,
        string Version,
        string MinecraftVersion,
        string Loader,
        string FileName,
        string Sha256,
        DateTimeOffset InstalledAt);
}
