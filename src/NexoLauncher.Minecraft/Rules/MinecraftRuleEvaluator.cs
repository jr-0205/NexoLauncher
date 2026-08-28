using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexoLauncher.Minecraft.Rules;

public static partial class MinecraftRuleEvaluator
{
    public static bool Allows(JsonElement element, IReadOnlySet<string>? enabledFeatures = null)
    {
        if (!element.TryGetProperty("rules", out var rules)) return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!Applies(rule, enabledFeatures)) continue;
            allowed = rule.GetProperty("action").GetString() == "allow";
        }
        return allowed;
    }

    private static bool Applies(JsonElement rule, IReadOnlySet<string>? enabledFeatures)
    {
        if (rule.TryGetProperty("os", out var os))
        {
            if (os.TryGetProperty("name", out var name) && name.GetString() != "windows") return false;
            if (os.TryGetProperty("arch", out var arch))
            {
                var current = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
                if (arch.GetString() != current) return false;
            }
            if (os.TryGetProperty("version", out var version) && !Regex.IsMatch(Environment.OSVersion.VersionString, version.GetString()!)) return false;
        }
        if (rule.TryGetProperty("features", out var features))
        {
            foreach (var feature in features.EnumerateObject())
            {
                var actual = enabledFeatures?.Contains(feature.Name) == true;
                if (actual != feature.Value.GetBoolean()) return false;
            }
        }
        return true;
    }
}
