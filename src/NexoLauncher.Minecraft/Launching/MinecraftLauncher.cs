using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexoLauncher.Minecraft.Installation;
using NexoLauncher.Minecraft.Rules;

namespace NexoLauncher.Minecraft.Launching;

public sealed class MinecraftLauncher(MinecraftPaths paths)
{
    public Process Launch(LaunchOptions options)
    {
        Validate(options);
        using var metadata = JsonDocument.Parse(File.ReadAllBytes(paths.VersionJson(options.VersionId)));
        var root = metadata.RootElement;
        if (!root.TryGetProperty("arguments", out var arguments)) throw new NotSupportedException("Esta versión antigua aún no es compatible.");
        Directory.CreateDirectory(paths.GameDirectory(options.VersionId));
        var classPath = BuildClassPath(root, options.VersionId);
        var values = Replacements(root, options, classPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = options.JavaExecutable,
            WorkingDirectory = paths.GameDirectory(options.VersionId),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-Xms512M");
        startInfo.ArgumentList.Add($"-Xmx{options.MemoryMiB}M");
        AddArguments(startInfo, arguments.GetProperty("jvm"), values);
        AddLoggingArgument(startInfo, root);
        startInfo.ArgumentList.Add(root.GetProperty("mainClass").GetString()!);
        AddArguments(startInfo, arguments.GetProperty("game"), values);
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Windows no pudo iniciar Java.");
    }

    private List<string> BuildClassPath(JsonElement root, string versionId)
    {
        var result = root.GetProperty("libraries").EnumerateArray()
            .Where(item => MinecraftRuleEvaluator.Allows(item))
            .Select(item => item.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact) ? artifact.GetProperty("path").GetString() : null)
            .Where(path => path is not null)
            .Select(path => Path.Combine(paths.Libraries, path!.Replace('/', Path.DirectorySeparatorChar))).ToList();
        result.Add(paths.ClientJar(versionId));
        return result;
    }

    private Dictionary<string, string> Replacements(JsonElement root, LaunchOptions options, List<string> classPath) => new()
    {
        ["${auth_player_name}"] = options.Username, ["${version_name}"] = options.VersionId,
        ["${game_directory}"] = paths.GameDirectory(options.VersionId), ["${assets_root}"] = paths.Assets,
        ["${assets_index_name}"] = root.GetProperty("assetIndex").GetProperty("id").GetString()!,
        ["${auth_uuid}"] = options.AccountId ?? OfflineUuid(options.Username), ["${auth_access_token}"] = options.AccessToken ?? "0",
        ["${clientid}"] = string.Empty, ["${auth_xuid}"] = string.Empty, ["${user_type}"] = options.AccessToken is null ? "legacy" : "msa",
        ["${version_type}"] = root.GetProperty("type").GetString() ?? "release", ["${natives_directory}"] = paths.Natives(options.VersionId),
        ["${launcher_name}"] = "NexoLauncher", ["${launcher_version}"] = "0.2.0",
        ["${classpath}"] = string.Join(Path.PathSeparator, classPath), ["${classpath_separator}"] = Path.PathSeparator.ToString(),
        ["${library_directory}"] = paths.Libraries
    };

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

    private static string Replace(string value, Dictionary<string, string> replacements) { foreach (var pair in replacements) value = value.Replace(pair.Key, pair.Value, StringComparison.Ordinal); return value; }
    private static void Validate(LaunchOptions options)
    {
        if (!File.Exists(options.JavaExecutable)) throw new FileNotFoundException("No se encontró Java.", options.JavaExecutable);
        if (options.MemoryMiB < 512) throw new ArgumentOutOfRangeException(nameof(options.MemoryMiB));
        if (options.Username.Length is < 3 or > 16 || options.Username.Any(character => !char.IsLetterOrDigit(character) && character != '_')) throw new ArgumentException("Usuario inválido.");
    }
    private static string OfflineUuid(string username) { var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username)); hash[6] = (byte)((hash[6] & 15) | 48); hash[8] = (byte)((hash[8] & 63) | 128); return Convert.ToHexString(hash).ToLowerInvariant(); }
}

