using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexoLauncher.Core.Installation;

namespace NexoLauncher.Infrastructure.Content;

public sealed record NexoInGameBuildTarget(
    string ProjectDirectory,
    string MinecraftVersion,
    string Loader,
    string NexoInGameVersion,
    string FileName,
    int JavaMajor,
    string GradleVersion,
    IReadOnlyList<NexoInGameArtifactDependency> Dependencies);

public sealed record NexoInGameBuildFailure(
    string MinecraftVersion,
    string Loader,
    string Message);

public sealed record NexoInGameBuildResult(
    string OutputDirectory,
    IReadOnlyList<NexoInGameArtifact> Artifacts,
    IReadOnlyList<NexoInGameBuildFailure> Failures);

/// <summary>
/// Herramienta de desarrollo local para producir los JAR de NEXO In-Game una sola vez.
/// Las builds terminadas se guardan bajo launcher/nexo-ingame y se publican en un
/// catalogo local verificado. Las instalaciones de usuario consumen esos artefactos;
/// nunca compilan Gradle por instancia.
/// </summary>
public sealed class NexoInGameBuildService
{
    private const string DefaultGradleVersion = "8.12";
    private const int DefaultJavaMajor = 21;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient http;
    private readonly NexoPaths paths;

    public NexoInGameBuildService(HttpClient http, NexoPaths paths)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string OutputRoot => Path.Combine(paths.Launcher, "nexo-ingame");

    public IReadOnlyList<NexoInGameBuildTarget> DiscoverTargets(string repositoryRoot)
    {
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var ingameRoot = Path.Combine(repositoryRoot, "ingame");
        if (!Directory.Exists(ingameRoot))
            throw new DirectoryNotFoundException($"No se encontro el directorio de fuentes NEXO In-Game: {ingameRoot}");

        var templateCatalog = LoadTemplateCatalog(repositoryRoot);
        var targets = new List<NexoInGameBuildTarget>();
        foreach (var projectDirectory in Directory.EnumerateDirectories(ingameRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var buildFile = Path.Combine(projectDirectory, "build.gradle");
            var propertiesFile = Path.Combine(projectDirectory, "gradle.properties");
            if (!File.Exists(buildFile) || !File.Exists(propertiesFile)) continue;

            var properties = ReadProperties(propertiesFile);
            if (!properties.TryGetValue("minecraft_version", out var minecraftVersion) || string.IsNullOrWhiteSpace(minecraftVersion))
                continue;
            if (!properties.TryGetValue("mod_version", out var modVersion) || string.IsNullOrWhiteSpace(modVersion))
                continue;
            if (!properties.TryGetValue("archives_base_name", out var archiveBaseName) || string.IsNullOrWhiteSpace(archiveBaseName))
                continue;

            var loader = properties.TryGetValue("nexo_loader", out var configuredLoader) && !string.IsNullOrWhiteSpace(configuredLoader)
                ? NormalizeLoader(configuredLoader)
                : InferLoader(Path.GetFileName(projectDirectory));
            var javaMajor = properties.TryGetValue("java_version", out var javaText) && int.TryParse(javaText, out var parsedJava)
                ? parsedJava
                : DefaultJavaMajor;
            var gradleVersion = properties.TryGetValue("gradle_version", out var gradleText) && !string.IsNullOrWhiteSpace(gradleText)
                ? gradleText
                : DefaultGradleVersion;
            var fileName = $"{archiveBaseName}-{modVersion}.jar";

            var template = templateCatalog?.Artifacts.FirstOrDefault(candidate =>
                string.Equals(candidate.MinecraftVersion, minecraftVersion, StringComparison.Ordinal) &&
                string.Equals(candidate.Loader, loader, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.NexoInGameVersion, modVersion, StringComparison.Ordinal));

            targets.Add(new NexoInGameBuildTarget(
                Path.GetFullPath(projectDirectory),
                minecraftVersion,
                loader,
                modVersion,
                template?.FileName ?? fileName,
                javaMajor,
                gradleVersion,
                template?.Dependencies?.ToArray() ?? []));
        }

        return targets
            .OrderBy(target => target.Loader, StringComparer.OrdinalIgnoreCase)
            .ThenBy(target => target.MinecraftVersion, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
            throw new InvalidOperationException("No se encontraron proyectos compilables de NEXO In-Game.");

        Directory.CreateDirectory(OutputRoot);
        var catalogArtifacts = new List<NexoInGameArtifact>();
        var failures = new List<NexoInGameBuildFailure>();

        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                progress?.Report($"Preparando {target.Loader} {target.MinecraftVersion}...");
                var javaExecutable = javaResolver(target.JavaMajor);
                if (string.IsNullOrWhiteSpace(javaExecutable) || !File.Exists(javaExecutable))
                    throw new InvalidOperationException($"No hay un Java {target.JavaMajor} utilizable para compilar esta build.");

                var gradle = await EnsureGradleAsync(target.GradleVersion, progress, token);
                await RunGradleBuildAsync(gradle, target, javaExecutable, progress, token);

                var builtJar = FindBuiltJar(target.ProjectDirectory);
                progress?.Report($"Empaquetando {target.Loader} {target.MinecraftVersion}...");
                var relativePath = Path.Combine(
                    target.NexoInGameVersion,
                    target.Loader.ToLowerInvariant(),
                    target.MinecraftVersion,
                    target.FileName);
                var destination = SafeChild(OutputRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".tmp";
                try
                {
                    File.Copy(builtJar, temporary, true);
                    var sha256 = await ComputeSha256Async(temporary, token);
                    File.Move(temporary, destination, true);

                    catalogArtifacts.Add(new NexoInGameArtifact(
                        target.MinecraftVersion,
                        target.Loader,
                        target.NexoInGameVersion,
                        "published",
                        target.FileName,
                        Path.GetRelativePath(OutputRoot, destination).Replace('\\', '/'),
                        null,
                        sha256,
                        DateTimeOffset.UtcNow,
                        target.Dependencies));
                    progress?.Report($"OK {target.Loader} {target.MinecraftVersion}: {target.FileName}");
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new NexoInGameBuildFailure(target.MinecraftVersion, target.Loader, exception.Message));
                catalogArtifacts.Add(new NexoInGameArtifact(
                    target.MinecraftVersion,
                    target.Loader,
                    target.NexoInGameVersion,
                    "planned",
                    target.FileName,
                    Path.Combine(
                        target.NexoInGameVersion,
                        target.Loader.ToLowerInvariant(),
                        target.MinecraftVersion,
                        target.FileName).Replace('\\', '/'),
                    null,
                    string.Empty,
                    null,
                    target.Dependencies));
                progress?.Report($"Fallo {target.Loader} {target.MinecraftVersion}: {FirstLine(exception.Message)}");
            }
        }

        var catalog = new NexoInGameArtifactCatalog(NexoInGameArtifactService.CatalogSchema, catalogArtifacts);
        await WriteCatalogAsync(catalog, token);
        return new NexoInGameBuildResult(OutputRoot, catalogArtifacts, failures);
    }

    private async Task<string> EnsureGradleAsync(string version, IProgress<string>? progress, CancellationToken token)
    {
        var toolsRoot = Path.Combine(paths.Cache, "devtools");
        var gradleHome = Path.Combine(toolsRoot, $"gradle-{SafeSegment(version)}");
        var gradleExecutable = Path.Combine(gradleHome, "bin", "gradle.bat");
        if (File.Exists(gradleExecutable)) return gradleExecutable;

        Directory.CreateDirectory(toolsRoot);
        var distributionName = $"gradle-{version}-bin.zip";
        var distributionUri = new Uri($"https://services.gradle.org/distributions/{distributionName}");
        var checksumUri = new Uri(distributionUri.AbsoluteUri + ".sha256");

        progress?.Report($"Descargando checksum de Gradle {version}...");
        using var checksumRequest = CreateRequest(checksumUri);
        using var checksumResponse = await http.SendAsync(checksumRequest, token);
        checksumResponse.EnsureSuccessStatusCode();
        var expectedSha = (await checksumResponse.Content.ReadAsStringAsync(token)).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        if (expectedSha.Length != 64 || !expectedSha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Gradle devolvio un checksum SHA-256 invalido.");

        var zipPath = Path.Combine(toolsRoot, distributionName);
        var temporaryZip = zipPath + ".download";
        if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
        try
        {
            progress?.Report($"Descargando Gradle {version}...");
            using var request = CreateRequest(distributionUri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporaryZip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, true))
            {
                var buffer = new byte[1024 * 128];
                long downloaded = 0;
                var lastReported = -1;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, token);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    downloaded += read;
                    var percent = total is > 0 ? (int)Math.Clamp(downloaded * 100 / total.Value, 0, 100) : -1;
                    if (percent >= 0 && (percent == 100 || percent >= lastReported + 5))
                    {
                        lastReported = percent;
                        progress?.Report($"Descargando Gradle {version}: {percent}% ({downloaded / 1024 / 1024} MB)...");
                    }
                }
            }

            progress?.Report($"Verificando Gradle {version}...");
            var actualSha = await ComputeSha256Async(temporaryZip, token);
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La distribucion de Gradle no supero la verificacion SHA-256.");
            File.Move(temporaryZip, zipPath, true);
        }
        finally
        {
            if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
        }

        progress?.Report($"Preparando Gradle {version}...");
        var staging = Path.Combine(toolsRoot, $".gradle-{SafeSegment(version)}-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zipPath, staging, overwriteFiles: true);
            var extractedHome = Path.Combine(staging, $"gradle-{version}");
            if (!File.Exists(Path.Combine(extractedHome, "bin", "gradle.bat")))
                throw new InvalidDataException("El ZIP de Gradle no contiene la estructura esperada.");
            if (Directory.Exists(gradleHome)) Directory.Delete(gradleHome, true);
            Directory.Move(extractedHome, gradleHome);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }

        if (!File.Exists(gradleExecutable))
            throw new FileNotFoundException("Gradle no quedo disponible despues de extraerlo.", gradleExecutable);
        return gradleExecutable;
    }

    private static async Task RunGradleBuildAsync(
        string gradleExecutable,
        NexoInGameBuildTarget target,
        string javaExecutable,
        IProgress<string>? progress,
        CancellationToken token)
    {
        progress?.Report($"Compilando {target.Loader} {target.MinecraftVersion}...");
        var javaBin = Path.GetDirectoryName(Path.GetFullPath(javaExecutable))
                      ?? throw new InvalidOperationException("No se pudo resolver bin/ del Java seleccionado.");
        var javaHome = Directory.GetParent(javaBin)?.FullName
                       ?? throw new InvalidOperationException("No se pudo resolver JAVA_HOME.");
        var log = new StringBuilder();
        var logLock = new object();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = gradleExecutable,
                WorkingDirectory = target.ProjectDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add(target.ProjectDirectory);
        process.StartInfo.ArgumentList.Add("clean");
        process.StartInfo.ArgumentList.Add("build");
        process.StartInfo.ArgumentList.Add("--no-daemon");
        process.StartInfo.ArgumentList.Add("--stacktrace");
        process.StartInfo.ArgumentList.Add("--console=plain");
        process.StartInfo.Environment["JAVA_HOME"] = javaHome;
        process.StartInfo.Environment["PATH"] = javaBin + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        process.StartInfo.Environment["GRADLE_OPTS"] = "-Dorg.gradle.internal.http.connectionTimeout=60000 -Dorg.gradle.internal.http.socketTimeout=120000";

        void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (logLock) log.AppendLine(line);
        }

        process.OutputDataReceived += (_, args) => Capture(args.Data);
        process.ErrorDataReceived += (_, args) => Capture(args.Data);
        if (!process.Start()) throw new InvalidOperationException("Windows no pudo iniciar Gradle.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(token);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            throw;
        }

        if (process.ExitCode == 0) return;
        string details;
        lock (logLock) details = Tail(log.ToString(), 7000);
        throw new InvalidOperationException(
            $"Gradle fallo para {target.Loader} {target.MinecraftVersion} con codigo {process.ExitCode}." +
            (string.IsNullOrWhiteSpace(details) ? string.Empty : Environment.NewLine + Environment.NewLine + details));
    }

    private static string FindBuiltJar(string projectDirectory)
    {
        var libs = Path.Combine(projectDirectory, "build", "libs");
        if (!Directory.Exists(libs))
            throw new DirectoryNotFoundException("Gradle termino sin crear build/libs.");
        return Directory.EnumerateFiles(libs, "*.jar", SearchOption.TopDirectoryOnly)
                   .Where(path =>
                   {
                       var name = Path.GetFileName(path);
                       return !name.Contains("sources", StringComparison.OrdinalIgnoreCase) &&
                              !name.Contains("javadoc", StringComparison.OrdinalIgnoreCase) &&
                              !name.Contains("-dev", StringComparison.OrdinalIgnoreCase);
                   })
                   .OrderByDescending(File.GetLastWriteTimeUtc)
                   .FirstOrDefault()
               ?? throw new InvalidDataException("La compilacion no produjo un JAR instalable.");
    }

    private NexoInGameArtifactCatalog? LoadTemplateCatalog(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "artifacts", "nexo-ingame", "catalog.json");
        if (!File.Exists(path)) return null;
        try
        {
            var catalog = JsonSerializer.Deserialize<NexoInGameArtifactCatalog>(File.ReadAllText(path), Json);
            return catalog?.SchemaVersion == NexoInGameArtifactService.CatalogSchema ? catalog : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task WriteCatalogAsync(NexoInGameArtifactCatalog catalog, CancellationToken token)
    {
        Directory.CreateDirectory(OutputRoot);
        var path = Path.Combine(OutputRoot, "catalog.json");
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(catalog, Json), token);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static Dictionary<string, string> ReadProperties(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return values;
    }

    private static string NormalizeLoader(string value) => value.Trim().ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        _ => throw new InvalidDataException($"Loader NEXO In-Game no soportado: {value}")
    };

    private static string InferLoader(string directoryName)
    {
        var prefix = directoryName.Split('-', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                     ?? throw new InvalidDataException("No se pudo inferir el loader del proyecto NEXO In-Game.");
        return NormalizeLoader(prefix);
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("NexoLauncher/0.5.2 (github.com/jr-0205/NexoLauncher)");
        return request;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private static string SafeChild(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        if (!candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La ruta de salida NEXO In-Game sale del directorio autorizado.");
        return candidate;
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Version invalida en herramienta de build NEXO In-Game.");
        return value;
    }

    private static string Tail(string value, int maximumLength)
    {
        value = value.Trim();
        return value.Length <= maximumLength ? value : value[^maximumLength..];
    }

    private static string FirstLine(string value)
    {
        var separator = value.IndexOfAny(['\r', '\n']);
        return separator < 0 ? value : value[..separator];
    }
}
