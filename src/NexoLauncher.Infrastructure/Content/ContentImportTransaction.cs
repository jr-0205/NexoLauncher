using System.Text.Json;

namespace NexoLauncher.Infrastructure.Content;

internal sealed class ContentImportTransaction : IDisposable
{
    private const string JournalName = "journal.json";
    private readonly string gameDirectory;
    private readonly string transactionRoot;
    private readonly string rollbackRoot;
    private bool completed;

    private ContentImportTransaction(string gameDirectory)
    {
        this.gameDirectory = NormalizeRoot(gameDirectory);
        EnsurePhysicalRoot(this.gameDirectory);
        var instanceRoot = Directory.GetParent(this.gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
            ?? throw new InvalidOperationException("No se pudo resolver la raíz de la instancia para crear staging.");
        EnsureDirectoryIsNotReparsePoint(instanceRoot, "La raíz de la instancia no puede ser un enlace o junction.");
        var runtimeRoot = Path.Combine(instanceRoot, "runtime");
        if (Directory.Exists(runtimeRoot)) EnsureDirectoryIsNotReparsePoint(runtimeRoot, "runtime/ no puede ser un enlace o junction.");
        Directory.CreateDirectory(runtimeRoot);
        var stagingRoot = Path.Combine(runtimeRoot, "import-staging");
        if (Directory.Exists(stagingRoot)) EnsureDirectoryIsNotReparsePoint(stagingRoot, "El staging de imports no puede ser un enlace o junction.");
        Directory.CreateDirectory(stagingRoot);
        transactionRoot = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        StagingGameDirectory = Path.Combine(transactionRoot, "game");
        rollbackRoot = Path.Combine(transactionRoot, "rollback");
        Directory.CreateDirectory(StagingGameDirectory);
        Directory.CreateDirectory(rollbackRoot);
    }

    public string StagingGameDirectory { get; }

    public static ContentImportTransaction Begin(string gameDirectory)
    {
        Recover(gameDirectory);
        return new ContentImportTransaction(gameDirectory);
    }

    public void Commit()
    {
        if (completed) throw new InvalidOperationException("La transacción de contenido ya terminó.");
        EnsurePhysicalRoot(gameDirectory);
        var files = Directory.EnumerateFiles(StagingGameDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new Entry(
                Path.GetRelativePath(StagingGameDirectory, path),
                File.Exists(SafeDestination(gameDirectory, Path.GetRelativePath(StagingGameDirectory, path)))))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var entry in files)
            EnsurePhysicalDestination(gameDirectory, SafeDestination(gameDirectory, entry.RelativePath));

        WriteJournal(new Journal("committing", files));
        try
        {
            foreach (var entry in files)
            {
                var source = SafeDestination(StagingGameDirectory, entry.RelativePath);
                var destination = SafeDestination(gameDirectory, entry.RelativePath);
                var backup = SafeDestination(rollbackRoot, entry.RelativePath);
                EnsurePhysicalDestination(gameDirectory, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);

                if (entry.HadOriginal)
                {
                    if (!File.Exists(destination))
                        throw new IOException($"El archivo que debía protegerse desapareció durante la importación: {entry.RelativePath}");
                    if (new FileInfo(destination).Attributes.HasFlag(FileAttributes.ReparsePoint))
                        throw new InvalidDataException($"El destino '{entry.RelativePath}' es un enlace y no puede modificarse.");
                    File.Replace(source, destination, backup, ignoreMetadataErrors: true);
                }
                else
                {
                    if (File.Exists(destination))
                        throw new IOException($"Otro proceso creó '{entry.RelativePath}' durante la importación; NEXO no lo sobrescribirá.");
                    File.Move(source, destination);
                }
            }

            WriteJournal(new Journal("committed", files));
            completed = true;
            TryDeleteDirectory(transactionRoot);
        }
        catch
        {
            var restored = RollbackStatic(gameDirectory, transactionRoot, new Journal("committing", files));
            completed = true;
            if (restored) TryDeleteDirectory(transactionRoot);
            throw;
        }
    }

    public static void Recover(string gameDirectory)
    {
        var game = NormalizeRoot(gameDirectory);
        EnsurePhysicalRoot(game);
        var instanceRoot = Directory.GetParent(game.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
        if (instanceRoot is null) return;
        EnsureDirectoryIsNotReparsePoint(instanceRoot, "La raíz de la instancia no puede ser un enlace o junction.");
        var stagingRoot = Path.Combine(instanceRoot, "runtime", "import-staging");
        if (!Directory.Exists(stagingRoot)) return;
        EnsureDirectoryIsNotReparsePoint(stagingRoot, "El staging de imports no puede ser un enlace o junction.");

        foreach (var transaction in Directory.EnumerateDirectories(stagingRoot))
        {
            if (new DirectoryInfo(transaction).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var journalPath = Path.Combine(transaction, JournalName);
            if (!File.Exists(journalPath))
            {
                try
                {
                    if (Directory.GetLastWriteTimeUtc(transaction) < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1)))
                        Directory.Delete(transaction, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                continue;
            }

            try
            {
                var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(journalPath));
                if (journal is null) continue;
                if (string.Equals(journal.State, "committed", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(transaction);
                    continue;
                }
                if (!string.Equals(journal.State, "committing", StringComparison.OrdinalIgnoreCase)) continue;
                if (RollbackStatic(game, transaction, journal)) TryDeleteDirectory(transaction);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // No borrar una transacción que no pudimos interpretar: sus backups pueden ser
                // la única copia de datos del usuario y requieren diagnóstico manual.
            }
        }
        TryDeleteIfEmpty(stagingRoot);
    }

    internal static void EnsurePhysicalDestination(string root, string destination)
    {
        var normalizedRoot = NormalizeRoot(root);
        var fullDestination = Path.GetFullPath(destination);
        if (!fullDestination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El destino intenta salir del gameDirectory autorizado.");

        var rootDirectory = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        EnsureDirectoryIsNotReparsePoint(rootDirectory, "gameDirectory no puede ser un enlace o junction.");
        var current = Directory.GetParent(fullDestination);
        while (current is not null && current.FullName.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("La ruta de contenido atraviesa un enlace o junction y fue bloqueada.");
            current = current.Parent;
        }
        if (File.Exists(fullDestination) && new FileInfo(fullDestination).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("El archivo de destino es un enlace y fue bloqueado.");
    }

    private static bool RollbackStatic(string gameDirectory, string transactionRoot, Journal journal)
    {
        var rollbackRoot = Path.Combine(transactionRoot, "rollback");
        var restored = true;
        foreach (var entry in journal.Files.Reverse())
        {
            var destination = SafeDestination(gameDirectory, entry.RelativePath);
            var backup = SafeDestination(rollbackRoot, entry.RelativePath);
            try
            {
                EnsurePhysicalDestination(gameDirectory, destination);
                if (entry.HadOriginal)
                {
                    if (!File.Exists(backup)) continue;
                    if (File.Exists(destination)) File.Delete(destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Move(backup, destination);
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (IOException) { restored = false; }
            catch (UnauthorizedAccessException) { restored = false; }
            catch (InvalidDataException) { restored = false; }
        }
        return restored;
    }

    private void WriteJournal(Journal journal)
    {
        var path = Path.Combine(transactionRoot, JournalName);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(journal, new JsonSerializerOptions { WriteIndented = true }));
        using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
            stream.Flush(flushToDisk: true);
        File.Move(temporary, path, true);
    }

    public void Dispose()
    {
        if (completed) return;
        completed = true;
        // Si todavía no existe journal no se publicó nada. Si existe, Recover es la autoridad
        // y conserva/recupera backups antes de borrar el staging.
        var journal = Path.Combine(transactionRoot, JournalName);
        if (!File.Exists(journal)) TryDeleteDirectory(transactionRoot);
    }

    private static string SafeDestination(string root, string relative)
    {
        root = NormalizeRoot(root);
        var normalized = relative.Replace('\\', '/');
        var platformRelative = normalized.Replace('/', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(platformRelative) ||
            normalized.Split('/').Any(part => part is "." or ".." || part.Contains(':')))
            throw new InvalidDataException("Ruta transaccional no válida.");
        var destination = Path.GetFullPath(Path.Combine(root, platformRelative));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La transacción intenta salir de su directorio autorizado.");
        return destination;
    }

    private static void EnsurePhysicalRoot(string root)
    {
        var directory = NormalizeRoot(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        EnsureDirectoryIsNotReparsePoint(directory, "gameDirectory no puede ser un enlace o junction.");
    }

    private static void EnsureDirectoryIsNotReparsePoint(string directory, string message)
    {
        if (Directory.Exists(directory) && new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException(message);
    }

    private static string NormalizeRoot(string value) =>
        Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static void TryDeleteDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteIfEmpty(string directory)
    {
        try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record Entry(string RelativePath, bool HadOriginal);
    private sealed record Journal(string State, IReadOnlyList<Entry> Files);
}
