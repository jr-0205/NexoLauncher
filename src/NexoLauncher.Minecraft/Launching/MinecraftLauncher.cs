using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexoLauncher.Minecraft.Installation;
using NexoLauncher.Minecraft.Rules;
using NexoLauncher.Minecraft.Security;

namespace NexoLauncher.Minecraft.Launching;

public sealed class MinecraftLauncher(MinecraftPaths paths)
{
    public MinecraftLaunchSession Launch(LaunchOptions options, LaunchPlan? plan = null)
    {
        Validate(options);
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(paths.VersionJson(options.VersionId)));
        var root = metadata.RootElement;
        if (!root.TryGetProperty("arguments", out var arguments)) throw new NotSupportedException("Esta versión antigua aún no es compatible.");
        var gameDirectory = Path.GetFullPath(plan?.GameDirectory ?? paths.GameDirectory(options.VersionId));
        Directory.CreateDirectory(gameDirectory);
        var nativesDirectory = PrepareNatives(root, gameDirectory, options.VersionId);
        var classPath = BuildClassPath(root, options.VersionId, plan?.AdditionalClassPath);
        var values = Replacements(root, options, classPath, gameDirectory, nativesDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = options.JavaExecutable,
            WorkingDirectory = gameDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-Xms512M");
        startInfo.ArgumentList.Add($"-Xmx{options.MemoryMiB}M");
        AddArguments(startInfo, arguments.GetProperty("jvm"), values);
        AddPlainArguments(startInfo, plan?.JvmArguments, values);
        AddPlainArguments(startInfo, options.JvmArguments, values);
        AddLoggingArgument(startInfo, root);
        startInfo.ArgumentList.Add(plan?.MainClass ?? root.GetProperty("mainClass").GetString()!);
        AddArguments(startInfo, arguments.GetProperty("game"), values);
        AddPlainArguments(startInfo, plan?.GameArguments, values);
        AddWindowArguments(startInfo, options);
        return StartCaptured(startInfo, options.VersionId, nativesDirectory);
    }

    private MinecraftLaunchSession StartCaptured(ProcessStartInfo startInfo, string versionId, string nativesDirectory)
    {
        var logs = paths.Logs;
        Directory.CreateDirectory(logs);
        var launchId = Guid.NewGuid().ToString("N");
        var logPath = Path.Combine(logs, $"minecraft-{SafeFileName(versionId)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{launchId[..8]}.log");
        var recent = new ConcurrentQueue<string>();
        var writer = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        writer.WriteLine($"NEXO Client 0.5.2 · Minecraft {versionId}");
        writer.WriteLine($"Java: {startInfo.FileName}");
        writer.WriteLine($"Directorio: {startInfo.WorkingDirectory}");
        writer.WriteLine($"Natives: {nativesDirectory}");

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows no pudo iniciar Java.");
            File.WriteAllText(Path.Combine(nativesDirectory, ".nexo-owner.pid"), process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            writer.Dispose();
            TryDeleteDirectory(nativesDirectory);
            throw;
        }

        var completion = CompleteOutputAsync();
        return new MinecraftLaunchSession(process, logPath, nativesDirectory, completion, recent);

        async Task CompleteOutputAsync()
        {
            try
            {
                await Task.WhenAll(
                    PumpAsync(process.StandardOutput, "OUT"),
                    PumpAsync(process.StandardError, "ERR"));
            }
            finally
            {
                lock (writer) writer.Dispose();
                TryDeleteDirectory(nativesDirectory);
            }
        }

        async Task PumpAsync(StreamReader reader, string source)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                var formatted = $"[{source}] {line}";
                recent.Enqueue(formatted);
                while (recent.Count > 60) recent.TryDequeue(out _);
                lock (writer) writer.WriteLine(formatted);
            }
        }
    }

    private List<string> BuildClassPath(JsonElement root, string versionId, IReadOnlyList<string>? additional)
    {
        var vanilla = root.GetProperty("libraries").EnumerateArray()
            .Where(item => MinecraftRuleEvaluator.Allows(item))
            .Select(item => item.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact) ? artifact.GetProperty("path").GetString() : null)
            .Where(path => path is not null)
            .Select(path => Path.Combine(paths.Libraries, path!.Replace('/', Path.DirectorySeparatorChar)));

        var ordered = new List<string>();
        var indexByArtifact = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void AddOrReplace(string path)
        {
            var key = LibraryArtifactIdentity.FromPath(path);
            if (indexByArtifact.TryGetValue(key, out var existingIndex)) ordered[existingIndex] = path;
            else { indexByArtifact[key] = ordered.Count; ordered.Add(path); }
        }

        foreach (var path in vanilla) AddOrReplace(path);
        ordered.Add(paths.ClientJar(versionId));
        if (additional is not null) foreach (var path in additional) AddOrReplace(path);
        return ordered;
    }

    private string PrepareNatives(JsonElement root, string gameDirectory, string versionId)
    {
        var nativesRoot = ResolveNativesRoot(gameDirectory, versionId);
        Directory.CreateDirectory(nativesRoot);
        CleanupOldNatives(nativesRoot);
        var launchDirectory = Path.Combine(nativesRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(launchDirectory);

        try
        {
            foreach (var library in root.GetProperty("libraries").EnumerateArray())
            {
                if (!MinecraftRuleEvaluator.Allows(library) ||
                    !library.TryGetProperty("natives", out var natives) ||
                    !natives.TryGetProperty("windows", out var classifierTemplate) ||
                    !library.TryGetProperty("downloads", out var downloads) ||
                    !downloads.TryGetProperty("classifiers", out var classifiers)) continue;

                var classifier = classifierTemplate.GetString()!.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                if (!classifiers.TryGetProperty(classifier, out var native) || !native.TryGetProperty("path", out var pathElement)) continue;
                var relativePath = pathElement.GetString() ?? throw new InvalidDataException("Ruta de native vacía.");
                var archive = Path.GetFullPath(Path.Combine(paths.Libraries, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!archive.StartsWith(WithSeparator(paths.Libraries), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Un native intenta salir de shared/libraries.");
                if (!File.Exists(archive)) throw new FileNotFoundException("Falta un archivo native compartido; NEXO debe reparar la versión.", archive);
                SafeArchiveExtractor.ExtractZip(archive, launchDirectory,
                    entry => !entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase));
            }
            return launchDirectory;
        }
        catch
        {
            TryDeleteDirectory(launchDirectory);
            throw;
        }
    }

    private string ResolveNativesRoot(string gameDirectory, string versionId)
    {
        var trimmed = gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(Path.GetFileName(trimmed), "game", StringComparison.OrdinalIgnoreCase))
        {
            var instanceRoot = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrWhiteSpace(instanceRoot)) return Path.Combine(instanceRoot, "runtime", "natives");
        }
        return paths.Natives(versionId);
    }

    private static void CleanupOldNatives(string nativesRoot)
    {
        if (!Directory.Exists(nativesRoot)) return;
        foreach (var directory in Directory.EnumerateDirectories(nativesRoot))
        {
            try
            {
                var ownerFile = Path.Combine(directory, ".nexo-owner.pid");
                if (File.Exists(ownerFile) && int.TryParse(File.ReadAllText(ownerFile), out var pid) && IsProcessAlive(pid)) continue;
                if (!File.Exists(ownerFile) && Directory.GetLastWriteTimeUtc(directory) > DateTime.UtcNow.Subtract(TimeSpan.FromDays(1))) continue;
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private Dictionary<string, string> Replacements(JsonElement root, LaunchOptions options, List<string> classPath, string gameDirectory, string nativesDirectory) => new()
    {
        ["${auth_player_name}"] = options.Username, ["${version_name}"] = options.VersionId,
        ["${game_directory}"] = gameDirectory, ["${assets_root}"] = paths.Assets,
        ["${assets_index_name}"] = root.GetProperty("assetIndex").GetProperty("id").GetString()!,
        ["${auth_uuid}"] = options.AccountId ?? OfflineUuid(options.Username), ["${auth_access_token}"] = options.AccessToken ?? "0",
        ["${clientid}"] = string.Empty, ["${auth_xuid}"] = string.Empty, ["${user_type}"] = options.AccessToken is null ? "legacy" : "msa",
        ["${version_type}"] = root.GetProperty("type").GetString() ?? "release", ["${natives_directory}"] = nativesDirectory,
        ["${launcher_name}"] = "NexoLauncher", ["${launcher_version}"] = "0.5.2",
        ["${classpath}"] = string.Join(Path.PathSeparator, classPath), ["${classpath_separator}"] = Path.PathSeparator.ToString(),
        ["${library_directory}"] = paths.Libraries
    };

    private static void AddPlainArguments(ProcessStartInfo info, IReadOnlyList<string>? arguments, Dictionary<string, string> values)
    {
        if (arguments is null) return;
        foreach (var argument in arguments.Where(value => !string.IsNullOrWhiteSpace(value)))
            info.ArgumentList.Add(Replace(argument, values));
    }

    private static void AddWindowArguments(ProcessStartInfo info, LaunchOptions options)
    {
        if (options.WindowWidth is > 0 && options.WindowHeight is > 0)
        {
            info.ArgumentList.Add("--width");
            info.ArgumentList.Add(options.WindowWidth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            info.ArgumentList.Add("--height");
            info.ArgumentList.Add(options.WindowHeight.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (options.Fullscreen) info.ArgumentList.Add("--fullscreen");
    }

    private void AddLoggingArgument(ProcessStartInfo info, JsonElement root)
    {
        if (!root.TryGetProperty("logging", out var logging) || !logging.TryGetProperty("client", out var client)) return;
        var argument = client.GetProperty("argument").GetString()!;
        var file = client.GetProperty("file").GetProperty("id").GetString()!;
        info.ArgumentList.Add(argument.Replace("${path}", Path.Combine(paths.Assets, "log_configs", file), StringComparison.Ordinal));
    }

    private static void AddArguments(ProcessStartInfo info, JsonElement arguments, Dictionary<string, string> values)
    {
        foreach (var argument in arguments.EnumerateArray())
        {
            if (argument.ValueKind == JsonValueKind.String) info.ArgumentList.Add(Replace(argument.GetString()!, values));
            else if (argument.ValueKind == JsonValueKind.Object && MinecraftRuleEvaluator.Allows(argument) && argument.TryGetProperty("value", out var value))
            {
                if (value.ValueKind == JsonValueKind.String) info.ArgumentList.Add(Replace(value.GetString()!, values));
                else foreach (var item in value.EnumerateArray()) info.ArgumentList.Add(Replace(item.GetString()!, values));
            }
        }
    }

    private static string Replace(string value, Dictionary<string, string> replacements)
    {
        foreach (var pair in replacements) value = value.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return value;
    }

    private static void Validate(LaunchOptions options)
    {
        if (!File.Exists(options.JavaExecutable)) throw new FileNotFoundException("No se encontró Java.", options.JavaExecutable);
        if (options.MemoryMiB < 512) throw new ArgumentOutOfRangeException(nameof(options.MemoryMiB));
        if (options.Username.Length is < 3 or > 16 || options.Username.Any(character => !char.IsLetterOrDigit(character) && character != '_')) throw new ArgumentException("Usuario inválido.");
    }

    private static string OfflineUuid(string username)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        hash[6] = (byte)((hash[6] & 15) | 48);
        hash[8] = (byte)((hash[8] & 63) | 128);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var result = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "minecraft" : result;
    }

    private static string WithSeparator(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static void TryDeleteDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class MinecraftLaunchSession(
    Process process,
    string logPath,
    string nativesDirectory,
    Task outputCompletion,
    ConcurrentQueue<string> recentOutput)
{
    public Process Process { get; } = process;
    public string LogPath { get; } = logPath;
    public string NativesDirectory { get; } = nativesDirectory;
    public Task OutputCompletion { get; } = outputCompletion;

    public async Task<string> GetFailureDetailsAsync(int maximumLines = 12)
    {
        try { await OutputCompletion.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { }

        var lines = recentOutput
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(Math.Max(1, maximumLines))
            .ToArray();
        return lines.Length == 0 ? "Java no produjo información de diagnóstico." : string.Join(Environment.NewLine, lines);
    }
}
