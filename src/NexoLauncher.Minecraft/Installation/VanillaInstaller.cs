using System.Text.Json;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Rules;
using NexoLauncher.Minecraft.Security;

namespace NexoLauncher.Minecraft.Installation;

public sealed class VanillaInstaller(VerifiedDownloader downloader, MinecraftPaths paths)
{
    public bool IsInstalled(string id) => File.Exists(paths.VersionJson(id)) && File.Exists(paths.ClientJar(id));

    public async Task InstallAsync(MinecraftVersion version, IProgress<InstallProgress>? progress = null, CancellationToken token = default)
    {
        paths.EnsureCreated();
        Directory.CreateDirectory(paths.VersionDirectory(version.Id));
        progress?.Report(new("Descargando metadatos", 0, 1));
        await downloader.DownloadAsync(version.MetadataUrl, paths.VersionJson(version.Id), null, token);
        using var metadata = JsonDocument.Parse(await File.ReadAllBytesAsync(paths.VersionJson(version.Id), token));
        var root = metadata.RootElement;
        var client = root.GetProperty("downloads").GetProperty("client");
        var jobs = new List<DownloadJob> { CreateJob(client, paths.ClientJar(version.Id)) };

        foreach (var library in root.GetProperty("libraries").EnumerateArray())
        {
            if (!MinecraftRuleEvaluator.Allows(library) || !library.TryGetProperty("downloads", out var downloads)) continue;
            if (downloads.TryGetProperty("artifact", out var artifact)) jobs.Add(CreateArtifactJob(artifact));
            if (library.TryGetProperty("natives", out var natives) && natives.TryGetProperty("windows", out var classifierTemplate)
                && downloads.TryGetProperty("classifiers", out var classifiers))
            {
                var classifier = classifierTemplate.GetString()!.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
                if (classifiers.TryGetProperty(classifier, out var native)) jobs.Add(CreateArtifactJob(native, true));
            }
        }

        var assetIndex = root.GetProperty("assetIndex");
        var indexId = assetIndex.GetProperty("id").GetString()!;
        var indexPath = Path.Combine(paths.Assets, "indexes", indexId + ".json");
        await downloader.DownloadAsync(assetIndex.GetProperty("url").GetString()!, indexPath, Optional(assetIndex, "sha1"), token);
        using var assets = JsonDocument.Parse(await File.ReadAllBytesAsync(indexPath, token));
        foreach (var asset in assets.RootElement.GetProperty("objects").EnumerateObject())
        {
            var hash = asset.Value.GetProperty("hash").GetString()!;
            jobs.Add(new DownloadJob($"https://resources.download.minecraft.net/{hash[..2]}/{hash}", Path.Combine(paths.Assets, "objects", hash[..2], hash), hash));
        }

        if (root.TryGetProperty("logging", out var logging) && logging.TryGetProperty("client", out var clientLogging))
        {
            var file = clientLogging.GetProperty("file");
            jobs.Add(new DownloadJob(file.GetProperty("url").GetString()!, Path.Combine(paths.Assets, "log_configs", file.GetProperty("id").GetString()!), Optional(file, "sha1")));
        }

        var completed = 0;
        await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token }, async (job, ct) =>
        {
            await downloader.DownloadAsync(job.Url, job.Path, job.Sha1, ct);
            progress?.Report(new("Descargando archivos", Interlocked.Increment(ref completed), jobs.Count));
        });

        var nativeDirectory = paths.Natives(version.Id);
        if (Directory.Exists(nativeDirectory)) Directory.Delete(nativeDirectory, true);
        Directory.CreateDirectory(nativeDirectory);
        foreach (var native in jobs.Where(job => job.IsNative))
            SafeArchiveExtractor.ExtractZip(native.Path, nativeDirectory, entry => !entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase));
        progress?.Report(new("Instalación lista", jobs.Count, jobs.Count));
    }

    private DownloadJob CreateArtifactJob(JsonElement artifact, bool native = false) => new(
        artifact.GetProperty("url").GetString()!,
        Path.Combine(paths.Libraries, artifact.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar)),
        Optional(artifact, "sha1"), native);
    private static DownloadJob CreateJob(JsonElement source, string target) => new(source.GetProperty("url").GetString()!, target, Optional(source, "sha1"));
    private static string? Optional(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.GetString() : null;
}


