using System.Net.Http;
using System.Text.Json;
using NexoLauncher.Core.Installation;

namespace NexoLauncher.Infrastructure.Content;

/// <summary>
/// Orquestador v2 de NEXA In-Game.
///
/// Mantiene el runner Gradle/verificador existente, pero deja de compilar sobre
/// los fuentes originales. Cada build se prepara en un workspace temporal con:
/// target + core común + proyectos companion/adaptadores declarados en
/// ingame/targets.json. Esto permite evolucionar el core una sola vez sin
/// duplicarlo por versión de Minecraft.
/// </summary>
public sealed class NexoInGameCompiler
{
    public const int ManifestSchema = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "build",
        ".gradle",
        ".idea",
        ".git",
        "out",
        "bin",
        "obj"
    };

    private readonly NexoPaths paths;
    private readonly NexoInGameBuildService runner;

    public NexoInGameCompiler(HttpClient http, NexoPaths paths)
    {
        ArgumentNullException.ThrowIfNull(http);
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        runner = new NexoInGameBuildService(http, paths);
    }

    public string OutputRoot => runner.OutputRoot;

    public IReadOnlyList<NexoInGameBuildTarget> DiscoverTargets(string repositoryRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var discovered = runner.DiscoverTargets(repositoryRoot);
        var manifest = LoadManifest(repositoryRoot);
        if (manifest is null) return discovered;

        var enabledProjects = manifest.Targets
            .Where(target => target.Enabled)
            .Select(target => NormalizeRelativeProject(target.Project))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return discovered
            .Where(target => enabledProjects.Contains(Path.GetFileName(target.ProjectDirectory)))
            .ToArray();
    }

    public async Task<NexoInGameBuildResult> BuildOneAsync(
        string repositoryRoot,
        string minecraftVersion,
        string loader,
        Func<int, string?> javaResolver,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(javaResolver);
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        minecraftVersion = minecraftVersion?.Trim() ?? string.Empty;
        loader = loader?.Trim() ?? string.Empty;

        var target = DiscoverTargets(repositoryRoot).FirstOrDefault(candidate =>
            string.Equals(candidate.MinecraftVersion, minecraftVersion, StringComparison.Ordinal) &&
            string.Equals(candidate.Loader, loader, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            throw new NotSupportedException($"No existe un target NEXA In-Game compilable para Minecraft {minecraftVersion} + {loader}.");

        var manifest = LoadManifest(repositoryRoot);
        if (manifest is null)
            return await runner.BuildOneAsync(repositoryRoot, minecraftVersion, loader, javaResolver, progress, token);

        var projectName = Path.GetFileName(target.ProjectDirectory);
        var definition = manifest.Targets.FirstOrDefault(candidate =>
            candidate.Enabled &&
            string.Equals(NormalizeRelativeProject(candidate.Project), projectName, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            throw new InvalidDataException($"targets.json no contiene el proyecto habilitado '{projectName}'.");

        progress?.Report($"Compiler v2 · preparando core + {definition.Adapter} para {target.MinecraftVersion}...");
        using var workspace = PrepareWorkspace(repositoryRoot, manifest, definition);
        progress?.Report($"Compiler v2 · workspace aislado listo para {target.Loader} {target.MinecraftVersion}.");

        return await runner.BuildOneAsync(
            workspace.RepositoryRoot,
            target.MinecraftVersion,
            target.Loader,
            javaResolver,
            progress,
            token);
    }

    public async Task<NexoInGameBuildResult> BuildAllAsync(
        string repositoryRoot,
        Func<int, string?> javaResolver,
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(javaResolver);
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var targets = DiscoverTargets(repositoryRoot);
        if (targets.Count == 0)
            throw new InvalidOperationException("No se encontraron targets NEXA In-Game compilables.");

        // Compilar target por target conserva el aislamiento de workspace y evita
        // que un proyecto companion sea interpretado accidentalmente como otra build.
        var artifacts = new List<NexoInGameArtifact>(targets.Count);
        var failures = new List<NexoInGameBuildFailure>();

        for (var index = 0; index < targets.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var target = targets[index];
            progress?.Report($"Compiler v2 · {index + 1}/{targets.Count}: {target.Loader} {target.MinecraftVersion}...");
            try
            {
                var result = await BuildOneAsync(
                    repositoryRoot,
                    target.MinecraftVersion,
                    target.Loader,
                    javaResolver,
                    progress,
                    token);
                artifacts.AddRange(result.Artifacts);
                failures.AddRange(result.Failures);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new NexoInGameBuildFailure(target.MinecraftVersion, target.Loader, exception.Message));
            }
        }

        return new NexoInGameBuildResult(OutputRoot, artifacts, failures);
    }

    public string? AdapterFor(string repositoryRoot, string minecraftVersion, string loader)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var manifest = LoadManifest(repositoryRoot);
        if (manifest is null) return null;
        var target = DiscoverTargets(repositoryRoot).FirstOrDefault(candidate =>
            string.Equals(candidate.MinecraftVersion, minecraftVersion, StringComparison.Ordinal) &&
            string.Equals(candidate.Loader, loader, StringComparison.OrdinalIgnoreCase));
        if (target is null) return null;
        var projectName = Path.GetFileName(target.ProjectDirectory);
        return manifest.Targets.FirstOrDefault(candidate =>
            candidate.Enabled &&
            string.Equals(NormalizeRelativeProject(candidate.Project), projectName, StringComparison.OrdinalIgnoreCase))?.Adapter;
    }

    private Workspace PrepareWorkspace(
        string repositoryRoot,
        CompilerManifest manifest,
        CompilerTarget target)
    {
        var ingameRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "ingame"));
        var workspaceRoot = Path.Combine(
            paths.Cache,
            "devtools",
            "nexa-ingame-workspaces",
            $"{SafeSegment(target.Adapter)}-{Guid.NewGuid():N}");
        var workspaceRepository = Path.Combine(workspaceRoot, "repo");
        var workspaceIngame = Path.Combine(workspaceRepository, "ingame");

        try
        {
            Directory.CreateDirectory(workspaceIngame);

            var coreSource = ResolveIngameChild(ingameRoot, manifest.CorePath);
            var coreDestination = Path.Combine(workspaceIngame, "core");
            if (!Directory.Exists(coreSource))
                throw new DirectoryNotFoundException($"El core común de NEXA no existe: {coreSource}");
            CopyDirectory(coreSource, coreDestination);

            CopyProject(ingameRoot, workspaceIngame, target.Project);
            foreach (var companion in target.Companions ?? [])
                CopyProject(ingameRoot, workspaceIngame, companion);

            CopyTemplateCatalog(repositoryRoot, workspaceRepository);

            var targetName = NormalizeRelativeProject(target.Project);
            var workspaceProject = Path.Combine(workspaceIngame, targetName);
            InjectCoreSourceSet(workspaceProject);

            return new Workspace(workspaceRepository, workspaceRoot);
        }
        catch
        {
            TryDeleteDirectory(workspaceRoot);
            throw;
        }
    }

    private static void CopyProject(string ingameRoot, string workspaceIngame, string relativeProject)
    {
        var projectName = NormalizeRelativeProject(relativeProject);
        var source = ResolveIngameChild(ingameRoot, projectName);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"El proyecto NEXA In-Game '{projectName}' no existe.");

        var destination = Path.Combine(workspaceIngame, projectName);
        if (Directory.Exists(destination)) return;
        CopyDirectory(source, destination);
    }

    private static void CopyTemplateCatalog(string repositoryRoot, string workspaceRepository)
    {
        var source = Path.Combine(repositoryRoot, "artifacts", "nexo-ingame", "catalog.json");
        if (!File.Exists(source)) return;
        var destinationDirectory = Path.Combine(workspaceRepository, "artifacts", "nexo-ingame");
        Directory.CreateDirectory(destinationDirectory);
        File.Copy(source, Path.Combine(destinationDirectory, "catalog.json"), overwrite: true);
    }

    private static void InjectCoreSourceSet(string projectDirectory)
    {
        var buildFile = Path.Combine(projectDirectory, "build.gradle");
        if (!File.Exists(buildFile))
            throw new FileNotFoundException("El target no contiene build.gradle.", buildFile);

        var current = File.ReadAllText(buildFile);
        if (current.Contains("../core/src/main/java", StringComparison.Ordinal)) return;

        var block = """

// NEXA_DYNAMIC_CORE - inyectado por NexoInGameCompiler v2.
sourceSets {
    main {
        java.srcDir '../core/src/main/java'
        resources.srcDir '../core/src/main/resources'
    }
}
""";
        File.WriteAllText(buildFile, current.TrimEnd() + block + Environment.NewLine);
    }

    private static CompilerManifest? LoadManifest(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "ingame", "targets.json");
        if (!File.Exists(path)) return null;
        try
        {
            var manifest = JsonSerializer.Deserialize<CompilerManifest>(File.ReadAllText(path), Json)
                           ?? throw new InvalidDataException("targets.json está vacío.");
            if (manifest.SchemaVersion != ManifestSchema)
                throw new InvalidDataException($"targets.json usa schema {manifest.SchemaVersion}; NEXA espera {ManifestSchema}.");
            if (string.IsNullOrWhiteSpace(manifest.CorePath))
                throw new InvalidDataException("targets.json no define corePath.");
            if (manifest.Targets is null || manifest.Targets.Count == 0)
                throw new InvalidDataException("targets.json no contiene targets.");
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("targets.json no contiene JSON válido.", exception);
        }
    }

    private static string ResolveIngameChild(string ingameRoot, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException("Ruta vacía en targets.json.");
        if (Path.IsPathRooted(relative) || relative.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Ruta no permitida en targets.json: {relative}");

        var normalizedRoot = Path.GetFullPath(ingameRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"La ruta de targets.json sale de ingame/: {relative}");
        return candidate;
    }

    private static string NormalizeRelativeProject(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException("Proyecto vacío en targets.json.");
        if (Path.IsPathRooted(value) || value.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Proyecto no permitido en targets.json: {value}");
        var normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Contains('/'))
            throw new InvalidDataException("Los targets deben ser directorios directos bajo ingame/.");
        return normalized;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (IgnoredDirectories.Contains(name)) continue;
            var info = new DirectoryInfo(directory);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException($"Compiler v2 rechazó un enlace/junction dentro de fuentes: {directory}");
            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    private static string SafeSegment(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..", StringComparison.Ordinal))
            return "target";
        return value.Replace(' ', '-').ToLowerInvariant();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record CompilerManifest(
        int SchemaVersion,
        string CorePath,
        IReadOnlyList<CompilerTarget> Targets);

    private sealed record CompilerTarget(
        string Project,
        string Adapter,
        bool Enabled,
        IReadOnlyList<string>? Companions);

    private sealed class Workspace : IDisposable
    {
        public Workspace(string repositoryRoot, string cleanupRoot)
        {
            RepositoryRoot = repositoryRoot;
            CleanupRoot = cleanupRoot;
        }

        public string RepositoryRoot { get; }
        private string CleanupRoot { get; }

        public void Dispose() => TryDeleteDirectory(CleanupRoot);
    }
}
