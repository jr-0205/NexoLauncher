using System.Diagnostics;
using NexoLauncher.Minecraft.Java;

namespace NexoLauncher.Minecraft.Launching;

/// <summary>
/// Construye una política de rendimiento conservadora para el proceso de Minecraft.
/// No sustituye argumentos explícitos del usuario y evita flags experimentales o
/// dependientes de un vendor concreto de Java.
/// </summary>
public static class MinecraftPerformanceTuner
{
    private const int MinimumInitialHeapMiB = 512;
    private const int MaximumInitialHeapMiB = 2048;

    public static MinecraftPerformancePlan Create(
        string versionId,
        int? metadataJavaMajor,
        int maximumHeapMiB,
        IReadOnlyList<string>? loaderJvmArguments = null,
        IReadOnlyList<string>? userJvmArguments = null)
    {
        if (maximumHeapMiB < MinimumInitialHeapMiB)
            throw new ArgumentOutOfRangeException(nameof(maximumHeapMiB));

        var javaMajor = metadataJavaMajor
                        ?? MinecraftJavaVersionPolicy.InferRequiredMajor(versionId)
                        ?? 8;

        // Un Xms demasiado pequeño obliga al heap a crecer varias veces durante la carga;
        // igualarlo a Xmx reserva memoria de más. Un cuarto del máximo, limitado a 2 GiB,
        // ofrece un punto medio seguro para clientes vanilla y modded.
        var initialHeap = Math.Clamp(
            maximumHeapMiB / 4,
            MinimumInitialHeapMiB,
            Math.Min(MaximumInitialHeapMiB, maximumHeapMiB));

        var explicitArguments = (loaderJvmArguments ?? [])
            .Concat(userJvmArguments ?? [])
            .Where(argument => !string.IsNullOrWhiteSpace(argument))
            .ToArray();

        var tuning = new List<string>();

        // Java moderno usa G1 por defecto. Lo declaramos sólo si el perfil no seleccionó
        // otro collector para que MaxGCPauseMillis y ParallelRefProc tengan una semántica
        // estable sin romper perfiles que deliberadamente usan ZGC/Shenandoah/etc.
        if (javaMajor >= 17 && !HasCollectorOverride(explicitArguments))
        {
            tuning.Add("-XX:+UseG1GC");
            AddUnlessOverridden(tuning, explicitArguments, "-XX:MaxGCPauseMillis=", "-XX:MaxGCPauseMillis=100");
            AddUnlessOverridden(tuning, explicitArguments, "-XX:+ParallelRefProcEnabled", "-XX:+ParallelRefProcEnabled");
        }

        return new MinecraftPerformancePlan(
            javaMajor,
            initialHeap,
            tuning,
            ProcessPriorityClass.AboveNormal);
    }

    private static bool HasCollectorOverride(IEnumerable<string> arguments)
        => arguments.Any(argument =>
            argument.StartsWith("-XX:+Use", StringComparison.OrdinalIgnoreCase) &&
            argument.EndsWith("GC", StringComparison.OrdinalIgnoreCase));

    private static void AddUnlessOverridden(
        ICollection<string> target,
        IEnumerable<string> explicitArguments,
        string prefix,
        string value)
    {
        if (!explicitArguments.Any(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            target.Add(value);
    }
}

public sealed record MinecraftPerformancePlan(
    int JavaMajor,
    int InitialHeapMiB,
    IReadOnlyList<string> JvmArguments,
    ProcessPriorityClass Priority)
{
    public string Summary =>
        $"Java {JavaMajor} · Xms {InitialHeapMiB} MiB · prioridad {Priority} · " +
        (JvmArguments.Count == 0 ? "JVM compatible" : $"{JvmArguments.Count} ajustes JVM");
}
