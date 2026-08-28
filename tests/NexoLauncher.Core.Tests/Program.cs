using NexoLauncher.Core.Configuration;
using NexoLauncher.Core.Launching;
using NexoLauncher.Minecraft.Security;
using System.IO.Compression;
using System.Text.Json;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Rules;
using NexoLauncher.Minecraft.Java;
using NexoLauncher.Minecraft.Loaders;
using NexoLauncher.Java;
using NexoLauncher.Java.Detection;
using NexoLauncher.Java.Compatibility;
using NexoLauncher.Java.Selection;
using NexoLauncher.Application.Configuration;
using NexoLauncher.Application.Instances;
using NexoLauncher.Domain.Configuration;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Configuration;
using NexoLauncher.Infrastructure.Instances;
using NexoLauncher.Infrastructure.Java;

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

Check("RAM recommendation scales conservatively with system memory", () =>
{
    Equal(2048, MemoryRecommendation.RecommendMiB(4096));
    Equal(4096, MemoryRecommendation.RecommendMiB(16384));
    Equal(8192, MemoryRecommendation.RecommendMiB(65536));
});

Check("RAM ceiling leaves memory for Windows", () =>
{
    Equal(6144, MemoryRecommendation.SafeMaximumMiB(8192));
    Equal(1024, MemoryRecommendation.SafeMaximumMiB(2048));
    Equal(32768, MemoryRecommendation.SafeMaximumMiB(65536));
});

Check("Java is automatic globally and only instances can override it", () =>
{
    var global = new LauncherSettings(4096, @"C:\Java\legacy-global\bin\java.exe", "Player");
    var inherited = LauncherSettingsResolver.Resolve(global, new InstanceSettings());
    Equal(4096, inherited.MemoryMiB);
    Equal<string?>(null, inherited.JavaPath);

    var overridden = LauncherSettingsResolver.Resolve(global, new InstanceSettings(6144, @"C:\Java\instance\bin\java.exe"));
    Equal(6144, overridden.MemoryMiB);
    Equal(@"C:\Java\instance\bin\java.exe", overridden.JavaPath);
});

Check("Launcher settings persist atomically", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var path = Path.Combine(root, "settings.json");
        var store = new JsonLauncherSettingsStore(path);
        store.SaveAsync(new LauncherSettings(6144, null, "NexoUser")).GetAwaiter().GetResult();
        var restored = store.LoadAsync().GetAwaiter().GetResult();
        Equal(6144, restored.MemoryMiB);
        Equal<string?>(null, restored.JavaPath);
        Equal("NexoUser", restored.Username);
        Equal(false, File.Exists(path + ".tmp"));
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

Check("Java runtime cache restores valid local runtimes", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var bin = Path.Combine(root, "jdk-21", "bin");
        Directory.CreateDirectory(bin);
        var java = Path.Combine(bin, "java.exe");
        var javaw = Path.Combine(bin, "javaw.exe");
        File.WriteAllText(java, "test");
        File.WriteAllText(javaw, "test");

        var cache = new JsonJavaRuntimeCache(Path.Combine(root, "java-runtimes.json"));
        cache.SaveAsync([new JavaRuntime(java, javaw, 21, "21.0.4", "Test Vendor", "amd64", "test")]).GetAwaiter().GetResult();
        var restored = cache.LoadAsync(TimeSpan.FromDays(1)).GetAwaiter().GetResult();

        Equal(1, restored.Count);
        Equal(21, restored[0].MajorVersion);
        Equal("Test Vendor", restored[0].Vendor);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

Check("Java selector chooses the runtime required by each Minecraft version", () =>
{
    JavaRuntime[] runtimes =
    [
        new(@"C:\Java\8\bin\java.exe", @"C:\Java\8\bin\javaw.exe", 8, "1.8.0_402", "Test 8", "amd64", "Program Files"),
        new(@"C:\Java\17\bin\java.exe", @"C:\Java\17\bin\javaw.exe", 17, "17.0.12", "Test 17", "amd64", "Program Files"),
        new(@"C:\Java\21\bin\java.exe", @"C:\Java\21\bin\javaw.exe", 21, "21.0.4", "Test 21", "amd64", "Program Files")
    ];

    Equal(8, JavaRuntimeSelector.Select(runtimes, 8)?.MajorVersion);
    Equal(17, JavaRuntimeSelector.Select(runtimes, 17)?.MajorVersion);
    Equal(21, JavaRuntimeSelector.Select(runtimes, 21)?.MajorVersion);
});

Check("Java selector reports a missing required major instead of using the wrong Java", () =>
{
    JavaRuntime[] runtimes =
    [
        new(@"C:\Java\17\bin\java.exe", @"C:\Java\17\bin\javaw.exe", 17, "17.0.12", "Test 17", "amd64", "Program Files"),
        new(@"C:\Java\21\bin\java.exe", @"C:\Java\21\bin\javaw.exe", 21, "21.0.4", "Test 21", "amd64", "Program Files")
    ];

    Equal<JavaRuntime?>(null, JavaRuntimeSelector.Select(runtimes, 8));
});

Check("Minecraft Java fallback matches release families", () =>
{
    Equal(8, MinecraftJavaVersionPolicy.InferRequiredMajor("1.16.5"));
    Equal(16, MinecraftJavaVersionPolicy.InferRequiredMajor("1.17.1"));
    Equal(17, MinecraftJavaVersionPolicy.InferRequiredMajor("1.20.4"));
    Equal(21, MinecraftJavaVersionPolicy.InferRequiredMajor("1.20.5"));
    Equal(21, MinecraftJavaVersionPolicy.InferRequiredMajor("1.21.1"));
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

Check("Instance Manager deletes only the selected isolated pack", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var selected = manager.CreateAsync("Pack temporal", "1.21.1").GetAwaiter().GetResult();
        var preserved = manager.CreateAsync("Pack conservado", "1.20.1").GetAwaiter().GetResult();
        var selectedDirectory = repository.GetInstanceDirectory(selected.Id);
        var preservedDirectory = repository.GetInstanceDirectory(preserved.Id);
        Directory.CreateDirectory(Path.Combine(selectedDirectory, "game", "saves", "Mundo"));
        File.WriteAllText(Path.Combine(selectedDirectory, "game", "saves", "Mundo", "level.dat"), "test");

        Equal(true, manager.DeleteAsync(selected.Id).GetAwaiter().GetResult());
        Equal(false, Directory.Exists(selectedDirectory));
        Equal(true, Directory.Exists(preservedDirectory));
        Equal<string?>(null, manager.GetAsync(selected.Id).GetAwaiter().GetResult());
        Equal("Pack conservado", manager.GetAsync(preserved.Id).GetAwaiter().GetResult()?.Name);
        Equal(false, manager.DeleteAsync(selected.Id).GetAwaiter().GetResult());
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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

Check("Java Manager rejects output without a valid version", () =>
{
    Equal<JavaRuntime?>(null, JavaRuntimeInspector.Parse("not a Java runtime", @"C:\Java\bin\java.exe", "test"));
});

Check("Java Manager rejects an incompatible major version", () =>
{
    var runtime = JavaRuntimeInspector.Parse("java.version = 17.0.12\njava.vendor = Test\nos.arch = amd64", @"C:\Java\bin\java.exe", "test")!;
    var result = JavaCompatibility.Evaluate(runtime, 21);
    Equal(false, result.IsCompatible);
    Equal(true, result.Message.Contains("Java 21", StringComparison.Ordinal));
});

Check("Fabric metadata exposes stable loader versions", () =>
{
    var bytes = """[{"loader":{"version":"0.16.14","stable":true}},{"loader":{"version":"0.16.13","stable":false}}]"""u8.ToArray();
    var versions = FabricMetadataClient.ParseLoaderVersions(bytes);
    Equal(2, versions.Count);
    Equal("0.16.14", versions[0].Version);
    Equal(true, versions[0].Stable);
});

Check("Fabric Maven coordinates resolve to a safe library path", () =>
{
    var resolved = FabricLibraryResolver.Resolve("net.fabricmc:fabric-loader:0.16.14");
    Equal("net/fabricmc/fabric-loader/0.16.14/fabric-loader-0.16.14.jar", resolved.RelativePath);
});

Check("Fabric Maven coordinates block path traversal", () =>
{
    var blocked = false;
    try { FabricLibraryResolver.Resolve("net.fabricmc:../escape:1.0"); }
    catch (InvalidDataException) { blocked = true; }
    Equal(true, blocked);
});

Check("Fabric instances persist their loader version", () =>
{
    var instance = GameInstance.Create("Fabric", "1.21.1", LoaderType.Fabric, "0.16.14");
    Equal(LoaderType.Fabric, instance.Loader);
    Equal("0.16.14", instance.LoaderVersion);
});

Check("Forge metadata filters versions for one Minecraft release", () =>
{
    var bytes = """<metadata><versioning><versions><version>1.20.1-47.3.0</version><version>1.21.1-52.0.1</version></versions></versioning></metadata>"""u8.ToArray();
    var versions = MavenMetadataParser.ParseVersions(bytes);
    Equal(2, versions.Count);
    Equal("1.20.1-47.3.0", versions[0]);
});

Check("NeoForge maps Minecraft releases to its official version line", () =>
{
    Equal("21.1.", InstallerLoaderMetadataClient.NeoForgePrefix("1.21.1"));
    Equal("20.6.", InstallerLoaderMetadataClient.NeoForgePrefix("1.20.6"));
    Equal<string?>(null, InstallerLoaderMetadataClient.NeoForgePrefix("1.19.4"));
});

Check("Forge and NeoForge instances persist loader versions", () =>
{
    var forge = GameInstance.Create("Forge", "1.20.1", LoaderType.Forge, "47.3.0");
    var neoForge = GameInstance.Create("NeoForge", "1.21.1", LoaderType.NeoForge, "21.1.200");
    Equal(LoaderType.Forge, forge.Loader);
    Equal("21.1.200", neoForge.LoaderVersion);
});

Check("Instance editor updates name and overrides atomically", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var created = manager.CreateAsync("Original", "1.21.1").GetAwaiter().GetResult();
        var updated = manager.UpdateAsync(created.Id, "Editada", new InstanceSettings(5120, null, ["-XX:+UseG1GC"], 1280, 720, false)).GetAwaiter().GetResult();
        Equal("Editada", updated.Name);
        Equal(5120, updated.Settings.MemoryMiB);
        Equal(1280, updated.Settings.WindowWidth);
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("33 checks passed.");
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
