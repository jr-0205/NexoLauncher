namespace NexoLauncher.Minecraft.Java;

public static class MinecraftJavaVersionPolicy
{
    public static int? InferRequiredMajor(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId)) return null;

        var numeric = versionId.Trim();
        if (!numeric.StartsWith("1.", StringComparison.Ordinal)) return null;

        var parts = numeric.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var minor)) return null;

        var patch = 0;
        if (parts.Length > 2)
        {
            var patchDigits = new string(parts[2].TakeWhile(char.IsDigit).ToArray());
            if (patchDigits.Length > 0) int.TryParse(patchDigits, out patch);
        }

        if (minor > 20 || (minor == 20 && patch >= 5)) return 21;
        if (minor >= 18) return 17;
        if (minor == 17) return 16;
        return 8;
    }
}
