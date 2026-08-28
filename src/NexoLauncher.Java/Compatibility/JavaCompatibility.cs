namespace NexoLauncher.Java.Compatibility;

public static class JavaCompatibility
{
    public static JavaCompatibilityResult Evaluate(JavaRuntime runtime, int requiredMajor)
    {
        if (requiredMajor <= 0) return new(false, "Minecraft no publicó un requisito de Java válido.");
        if (runtime.MajorVersion != requiredMajor)
            return new(false, $"Esta versión necesita Java {requiredMajor}; seleccionaste Java {runtime.MajorVersion}.");
        if (Environment.Is64BitOperatingSystem && !runtime.Is64Bit)
            return new(false, "Selecciona un Java de 64 bits para evitar límites de memoria.");
        if (!File.Exists(runtime.JavawExecutable))
            return new(false, "El runtime no incluye javaw.exe.");
        return new(true, $"Java {runtime.MajorVersion} ({runtime.Architecture}) compatible.");
    }
}
