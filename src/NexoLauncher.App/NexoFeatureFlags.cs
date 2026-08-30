namespace NexoLauncher.App;

internal static class NexoFeatureFlags
{
#if DEBUG
    public const bool DeveloperTools = true;
#else
    public const bool DeveloperTools = false;
#endif

    // Reservados para la fase de autenticación/premium. No deben aparecer como módulos muertos
    // antes de que exista una sesión Microsoft/Minecraft de producción.
    public const bool Accounts = false;
    public const bool Premium = false;
    public const bool Social = false;
}
