namespace NexoLauncher.App;

internal static class NexoFeatureFlags
{
#if DEBUG
    public static readonly bool DeveloperTools = true;
#else
    public static readonly bool DeveloperTools = false;
#endif

    // Reservados para la fase de autenticación/premium. No deben aparecer como módulos muertos
    // antes de que exista una sesión Microsoft/Minecraft de producción.
    public static readonly bool Accounts = false;
    public static readonly bool Premium = false;
    public static readonly bool Social = false;
}
