namespace NexoLauncher.Minecraft.Downloads;

public static class DownloadJobPlanner
{
    public static IReadOnlyList<DownloadJob> Deduplicate(IEnumerable<DownloadJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var ordered = new List<DownloadJob>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in jobs)
        {
            var destination = Path.GetFullPath(job.Path);
            if (!indexes.TryGetValue(destination, out var index))
            {
                indexes[destination] = ordered.Count;
                ordered.Add(job with { Path = destination });
                continue;
            }

            var existing = ordered[index];
            if (!string.Equals(existing.Url, job.Url, StringComparison.Ordinal) ||
                !string.Equals(existing.Sha1, job.Sha1, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Dos descargas incompatibles apuntan al mismo archivo: {destination}");

            if (job.IsNative && !existing.IsNative)
                ordered[index] = existing with { IsNative = true };
        }

        return ordered;
    }
}
