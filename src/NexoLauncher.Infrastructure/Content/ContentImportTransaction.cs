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
        var instanceRoot = Directory.GetParent(this.gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName
            ?? throw new InvalidOperationException("No se pudo resolver la raíz de la instancia para crear staging.");
        var stagingRoot = Path.Combine(instanceRoot, "runtime", "import-staging");
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
        var files = Directory.EnumerateFiles(StagingGameDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new Entry(
                Path.GetRelativePath(StagingGameDirectory, path),
                File.Exists(SafeDestination(gameDirectory, Path.GetRelativePath(StagingGameDirectory, path)))))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        WriteJournal(new Journal("committing", files));
        try
        {
            foreach (var entry in files)
            {
                var source = SafeDestination(StagingGameDirectory, entry.RelativePath);
                var destination = SafeDestination(gameDirectory, entry.RelativePath);
                var backup = SafeDestination(rollbackRoot, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);

                if (entry.HadOriginal)
                {
                    if (!File.Exists(destination))
                        throw new IOException($"El archivo que debía protegerse desapareció durante la importación: {entry.RelativePath}");
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
        var instanceRoot = Directory.GetParent(game.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))?.FullName;
        if (instanceRoot is null) return;
        var stagingRoot = Path.Combine(instanceRoot, "runtime", "import-staging");
        if (!Directory.Exists(stagingRoot)) return;

        foreach (var transaction in Directory.EnumerateDirectories(stagingRoot))
        {
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
