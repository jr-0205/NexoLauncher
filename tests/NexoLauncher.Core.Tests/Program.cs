using NexoLauncher.Core.Configuration;
using NexoLauncher.Core.Launching;
using NexoLauncher.Minecraft.Security;
using System.IO.Compression;
using System.Text.Json;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Rules;
using NexoLauncher.Java.Detection;
using NexoLauncher.Java.Compatibility;
using NexoLauncher.Application.Instances;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Instances;

var failures = new List<string>();

Check("RAM is capped while reserving memory for Windows", () =>
{
    var settings = MemorySettings.CreateSafe(16_384, 8_192);
    Equal(6_144, settings.MaximumMiB);
});

Check("RAM never drops below the supported minimum", () =>
{
    var settings = MemorySettings.CreateSafe(128, 8_192);
    Equal(512, settings.MaximumMiB);
});

Check("Launch arguments stay tokenized", () =>
{
    var request = new LaunchRequest(
        @"C:\Program Files\Java\bin\javaw.exe",
        @"C:\Games\Nexo Instance",
        "net.minecraft.client.main.Main",
        [@"C:\Games\Nexo Instance\client.jar"],
        ["--username", "Player One"],
        512,
        4096);
    var info = MinecraftProcessFactory.CreateStartInfo(request);
    Equal(7, info.ArgumentList.Count);
    Equal("Player One", info.ArgumentList[6]);
});

Check("Safe ZIP extraction accepts files inside the destination", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var zipPath = Path.Combine(root, "safe.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("native/example.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("test");
        }
        var output = Path.Combine(root, "output");
        SafeArchiveExtractor.ExtractZip(zipPath, output);
        Equal(true, File.Exists(Path.Combine(output, "native", "example.dll")));
    }
    finally { Directory.Delete(root, true); }
});

Check("Safe ZIP extraction blocks path traversal", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var zipPath = Path.Combine(root, "malicious.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escaped.dll");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("blocked");
        }
        var blocked = false;
        try { SafeArchiveExtractor.ExtractZip(zipPath, Path.Combine(root, "output")); }
        catch (InvalidDataException) { blocked = true; }
        Equal(true, blocked);
        Equal(false, File.Exists(Path.Combine(root, "escaped.dll")));
    }
    finally { Directory.Delete(root, true); }
});

Check("Instance Manager persists and restores an isolated profile", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var created = manager.CreateAsync("Vanilla principal", "1.21.1").GetAwaiter().GetResult();
        var restored = repository.GetAsync(created.Id).GetAwaiter().GetResult();
        Equal("Vanilla principal", restored?.Name);
        Equal("1.21.1", restored?.MinecraftVersion);
        Equal(LoaderType.Vanilla, restored?.Loader);
        Equal(true, File.Exists(Path.Combine(repository.GetInstanceDirectory(created.Id), "instance.json")));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

Check("Instance Manager persists Java and RAM overrides", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var created = manager.CreateAsync("Perfil runtime", "1.21.1").GetAwaiter().GetResult();
        manager.UpdateSettingsAsync(created.Id, created.Settings with
        {
            MemoryMiB = 6144,
            JavaPath = @"C:\Java\jdk-21\bin\java.exe"
        }).GetAwaiter().GetResult();

        var restored = manager.GetAsync(created.Id).GetAwaiter().GetResult();
        Equal(6144, restored?.Settings.MemoryMiB);
        Equal(@"C:\Java\jdk-21\bin\java.exe", restored?.Settings.JavaPath);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

Check("Instance Manager supports multiple profiles for one Minecraft version", () =>
{
    var first = GameInstance.Create("Perfil A", "1.21.1");
    var second = GameInstance.Create("Perfil B", "1.21.1");
    Equal(false, first.Id == second.Id);
    Equal("1.21.1", first.MinecraftVersion);
    Equal("1.21.1", second.MinecraftVersion);
});

Check("Legacy migration creates one profile without moving game files", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var legacy = Path.Combine(root, "1.21.1");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "1.21.1.json"), "{}");
        File.WriteAllText(Path.Combine(legacy, "1.21.1.jar"), "test");
        var repository = new JsonInstanceRepository(root);
        var migrator = new LegacyInstallationMigrator(root, repository);
        Equal(1, migrator.MigrateAsync().GetAwaiter().GetResult());
        Equal(0, migrator.MigrateAsync().GetAwaiter().GetResult());
        Equal(1, repository.ListAsync().GetAwaiter().GetResult().Count);
        Equal(true, File.Exists(Path.Combine(legacy, "1.21.1.jar")));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

Check("Minecraft rules apply the last matching Windows rule", () =>
{
    using var json = JsonDocument.Parse("""{"rules":[{"action":"disallow"},{"action":"allow","os":{"name":"windows"}}]}""");
    Equal(true, MinecraftRuleEvaluator.Allows(json.RootElement));
});

Check("Minecraft feature rules remain disabled unless requested", () =>
{
    using var json = JsonDocument.Parse("""{"rules":[{"action":"allow","features":{"has_custom_resolution":true}}]}""");
    Equal(false, MinecraftRuleEvaluator.Allows(json.RootElement));
    Equal(true, MinecraftRuleEvaluator.Allows(json.RootElement, new HashSet<string> { "has_custom_resolution" }));
});

Check("Verified downloader rejects insecure HTTP", () =>
{
    using var http = new HttpClient();
    var downloader = new VerifiedDownloader(http);
    var rejected = false;
    try { downloader.DownloadAsync("http://example.test/file", Path.Combine(Path.GetTempPath(), "nexo-never-write"), null).GetAwaiter().GetResult(); }
    catch (InvalidDataException) { rejected = true; }
    Equal(true, rejected);
});

Check("Java Manager parses a modern runtime", () =>
{
    var output = "java.version = 21.0.4\njava.vendor = Eclipse Adoptium\nos.arch = amd64\n";
    var runtime = JavaRuntimeInspector.Parse(output, @"C:\Java\bin\java.exe", "test");
    Equal(21, runtime?.MajorVersion);
    Equal("Eclipse Adoptium", runtime?.Vendor);
    Equal(true, runtime?.Is64Bit);
});

Check("Java Manager parses legacy Java 8 notation", () =>
{
    Equal(8, JavaRuntimeInspector.ParseMajor("1.8.0_402"));
});

Check("Java Manager rejects an incompatible major version", () =>
{
    var runtime = JavaRuntimeInspector.Parse("java.version = 17.0.12\njava.vendor = Test\nos.arch = amd64", @"C:\Java\bin\java.exe", "test")!;
    var result = JavaCompatibility.Evaluate(runtime, 21);
    Equal(false, result.IsCompatible);
    Equal(true, result.Message.Contains("Java 21", StringComparison.Ordinal));
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("15 checks passed.");
return 0;

void Check(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine($"PASS: {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL: {name}: {exception.Message}");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
