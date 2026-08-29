using NexoLauncher.Core.Configuration;
using NexoLauncher.Core.Installation;
using NexoLauncher.Core.Launching;
using NexoLauncher.Minecraft.Security;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexoLauncher.Minecraft;
using NexoLauncher.Minecraft.Downloads;
using NexoLauncher.Minecraft.Rules;
using NexoLauncher.Minecraft.Java;
using NexoLauncher.Minecraft.Loaders;
using NexoLauncher.Minecraft.Launching;
using NexoLauncher.Java;
using NexoLauncher.Java.Detection;
using NexoLauncher.Java.Compatibility;
using NexoLauncher.Java.Selection;
using NexoLauncher.Application.Configuration;
using NexoLauncher.Application.Instances;
using NexoLauncher.Domain.Configuration;
using NexoLauncher.Domain.Instances;
using NexoLauncher.Infrastructure.Configuration;
using NexoLauncher.Infrastructure.Content;
using NexoLauncher.Infrastructure.Instances;
using NexoLauncher.Infrastructure.Java;

var failures = new List<string>();
var passed = 0;

Check("NEXO paths use one shared topology under LocalApplicationData", () =>
{
    var paths = NexoPaths.ForCurrentUser();
    var expectedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoLauncher");
    Equal(expectedRoot, paths.Root);
    Equal(Path.Combine(expectedRoot, "shared"), paths.Shared);
    Equal(Path.Combine(expectedRoot, "shared", "assets"), paths.Assets);
    Equal(Path.Combine(expectedRoot, "shared", "libraries"), paths.Libraries);
    Equal(Path.Combine(expectedRoot, "shared", "versions"), paths.Versions);
    Equal(Path.Combine(expectedRoot, "shared", "runtimes"), paths.Runtime);
    Equal(Path.Combine(expectedRoot, "shared", "runtimes", "java"), paths.JavaRuntimes);
    Equal(Path.Combine(expectedRoot, "instances"), paths.Instances);
    Equal(Path.Combine(expectedRoot, "cache"), paths.Cache);
    Equal(Path.Combine(expectedRoot, "logs", "launcher"), paths.LauncherLogs);
});

Check("Instance paths use GUID identity and isolate all mutable roots", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-instance-paths");
    var id = Guid.NewGuid();
    var paths = new InstancePaths(root, id);
    Equal(Path.Combine(Path.GetFullPath(root), id.ToString("N")), paths.Root);
    Equal(Path.Combine(paths.Root, "game"), paths.Game);
    Equal(Path.Combine(paths.Game, "mods"), paths.Mods);
    Equal(Path.Combine(paths.Game, "config"), paths.Config);
    Equal(Path.Combine(paths.Game, "saves"), paths.Saves);
    Equal(Path.Combine(paths.Game, "logs"), paths.GameLogs);
    Equal(Path.Combine(paths.Game, "crash-reports"), paths.CrashReports);
    Equal(Path.Combine(paths.Root, "runtime", "natives"), paths.Natives);
    Equal(Path.Combine(paths.Root, "backups"), paths.Backups);
});

Check("Per-launch native directories never collide", () =>
{
    var instance = new InstancePaths(Path.Combine(Path.GetTempPath(), "nexo-native-isolation"), Guid.NewGuid());
    var first = instance.NativesLaunch(Guid.NewGuid());
    var second = instance.NativesLaunch(Guid.NewGuid());
    Equal(false, string.Equals(first, second, StringComparison.OrdinalIgnoreCase));
    Equal(true, first.StartsWith(instance.Natives + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    Equal(true, second.StartsWith(instance.Natives + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
});

Check("Classpath identity keeps native classifiers separate", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-library-identity");
    var regular = Path.Combine(root, "org", "lwjgl", "lwjgl-glfw", "3.3.3", "lwjgl-glfw-3.3.3.jar");
    var native = Path.Combine(root, "org", "lwjgl", "lwjgl-glfw", "3.3.3", "lwjgl-glfw-3.3.3-natives-windows.jar");
    var newer = Path.Combine(root, "org", "lwjgl", "lwjgl-glfw", "3.3.4", "lwjgl-glfw-3.3.4.jar");
    Equal(LibraryArtifactIdentity.FromPath(regular), LibraryArtifactIdentity.FromPath(newer));
    Equal(false, LibraryArtifactIdentity.FromPath(regular) == LibraryArtifactIdentity.FromPath(native));
});

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
    var root = TempRoot();
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
    finally { DeleteRoot(root); }
});

Check("Java runtime cache restores valid local runtimes", () =>
{
    var root = TempRoot();
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
    finally { DeleteRoot(root); }
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
    var request = new LaunchRequest(@"C:\Program Files\Java\bin\javaw.exe", @"C:\Games\Nexo Instance",
        "net.minecraft.client.main.Main", [@"C:\Games\Nexo Instance\client.jar"], ["--username", "Player One"], 512, 4096);
    var info = MinecraftProcessFactory.CreateStartInfo(request);
    Equal(7, info.ArgumentList.Count);
    Equal("Player One", info.ArgumentList[6]);
});

Check("Safe ZIP extraction accepts files inside the destination", () =>
{
    var root = TempRoot();
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
    finally { DeleteRoot(root); }
});

Check("Safe ZIP extraction blocks path traversal", () =>
{
    var root = TempRoot();
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
    finally { DeleteRoot(root); }
});

Check("Instance Manager persists and restores a GUID-isolated profile", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var created = manager.CreateAsync("Vanilla principal", "1.21.1").GetAwaiter().GetResult();
        var restored = repository.GetAsync(created.Id).GetAwaiter().GetResult();
        Equal("Vanilla principal", restored?.Name);
        Equal("1.21.1", restored?.MinecraftVersion);
        Equal(LoaderType.Vanilla, restored?.Loader);
        Equal(Path.Combine(root, created.Id.ToString()), repository.GetInstanceDirectory(created.Id));
        Equal(true, File.Exists(Path.Combine(repository.GetInstanceDirectory(created.Id), "instance.json")));
    }
    finally { DeleteRoot(root); }
});

Check("Instance manifest schema is explicit and stores relative gameDirectory", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var created = manager.CreateAsync("Schema", "1.21.1", LoaderType.Fabric, "0.16.14").GetAwaiter().GetResult();
        manager.UpdateSettingsAsync(created.Id, new InstanceSettings(6144, null)).GetAwaiter().GetResult();
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository.GetInstanceDirectory(created.Id), "instance.json")));
        var manifest = document.RootElement;
        Equal(JsonInstanceRepository.CurrentSchemaVersion, manifest.GetProperty("schemaVersion").GetInt32());
        Equal(created.Id.ToString(), manifest.GetProperty("id").GetString());
        Equal("fabric", manifest.GetProperty("loader").GetProperty("type").GetString());
        Equal("automatic", manifest.GetProperty("java").GetProperty("mode").GetString());
        Equal(6144, manifest.GetProperty("memory").GetProperty("maxMb").GetInt32());
        Equal("game", manifest.GetProperty("gameDirectory").GetString());
        Equal(false, manifest.TryGetProperty("directoryName", out _));
    }
    finally { DeleteRoot(root); }
});

Check("Instance Manager persists Java and RAM overrides", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var created = manager.CreateAsync("Perfil runtime", "1.21.1").GetAwaiter().GetResult();
        manager.UpdateSettingsAsync(created.Id, created.Settings with { MemoryMiB = 6144, JavaPath = @"C:\Java\jdk-21\bin\java.exe" }).GetAwaiter().GetResult();
        var restored = manager.GetAsync(created.Id).GetAwaiter().GetResult();
        Equal(6144, restored?.Settings.MemoryMiB);
        Equal(@"C:\Java\jdk-21\bin\java.exe", restored?.Settings.JavaPath);
    }
    finally { DeleteRoot(root); }
});

Check("Instance Manager supports multiple profiles for one Minecraft version", () =>
{
    var first = GameInstance.Create("Perfil A", "1.21.1");
    var second = GameInstance.Create("Perfil B", "1.21.1");
    Equal(false, first.Id == second.Id);
    Equal("1.21.1", first.MinecraftVersion);
    Equal("1.21.1", second.MinecraftVersion);
    Equal(first.Id.ToString(), first.DirectoryName);
    Equal(second.Id.ToString(), second.DirectoryName);
});

Check("Profiles with the same visible name and loader remain isolated", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var first = manager.CreateAsync("Survival", "1.21.1", LoaderType.Fabric, "0.16.14").GetAwaiter().GetResult();
        var second = manager.CreateAsync("Survival", "1.21.1", LoaderType.Fabric, "0.16.14").GetAwaiter().GetResult();
        Equal(Path.Combine(root, first.Id.ToString()), repository.GetInstanceDirectory(first.Id));
        Equal(Path.Combine(root, second.Id.ToString()), repository.GetInstanceDirectory(second.Id));
        Equal(false, repository.GetInstanceDirectory(first.Id) == repository.GetInstanceDirectory(second.Id));
        Equal(2, manager.ListAsync().GetAwaiter().GetResult().Count);
    }
    finally { DeleteRoot(root); }
});

Check("Renaming a profile never changes its physical directory", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var profile = manager.CreateAsync("Pack antiguo", "1.21.1", LoaderType.Fabric, "0.16.14").GetAwaiter().GetResult();
        var before = repository.GetInstanceDirectory(profile.Id);
        Directory.CreateDirectory(Path.Combine(before, "game", "mods"));
        File.WriteAllText(Path.Combine(before, "game", "mods", "example.jar"), "test");
        var updated = manager.UpdateAsync(profile.Id, "Pack nuevo", profile.Settings).GetAwaiter().GetResult();
        var after = repository.GetInstanceDirectory(profile.Id);
        Equal(before, after);
        Equal("Pack nuevo", updated.Name);
        Equal(true, File.Exists(Path.Combine(after, "game", "mods", "example.jar")));
    }
    finally { DeleteRoot(root); }
});

Check("Mutable mods configs saves and options stay independent", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var a = manager.CreateAsync("A", "1.21.1", LoaderType.Fabric, "0.16.14").GetAwaiter().GetResult();
        var b = manager.CreateAsync("B", "1.21.1", LoaderType.Fabric, "0.16.14").GetAwaiter().GetResult();
        var ap = repository.GetPaths(a.Id);
        var bp = repository.GetPaths(b.Id);
        File.WriteAllText(Path.Combine(ap.Mods, "A.jar"), "A");
        File.WriteAllText(Path.Combine(bp.Mods, "B.jar"), "B");
        File.WriteAllText(Path.Combine(ap.Config, "a.json"), "A");
        File.WriteAllText(Path.Combine(bp.Config, "b.json"), "B");
        Directory.CreateDirectory(Path.Combine(ap.Saves, "WorldA"));
        Directory.CreateDirectory(Path.Combine(bp.Saves, "WorldB"));
        File.WriteAllText(Path.Combine(ap.Game, "options.txt"), "a=true");
        File.WriteAllText(Path.Combine(bp.Game, "options.txt"), "b=true");
        Equal(false, File.Exists(Path.Combine(ap.Mods, "B.jar")));
        Equal(false, File.Exists(Path.Combine(bp.Mods, "A.jar")));
        Equal(false, File.Exists(Path.Combine(ap.Config, "b.json")));
        Equal(false, Directory.Exists(Path.Combine(bp.Saves, "WorldA")));
        Equal("a=true", File.ReadAllText(Path.Combine(ap.Game, "options.txt")));
        Equal("b=true", File.ReadAllText(Path.Combine(bp.Game, "options.txt")));
    }
    finally { DeleteRoot(root); }
});

Check("Deleting one instance preserves sibling profile and all shared resources", () =>
{
    var root = TempRoot();
    try
    {
        var paths = new NexoPaths(root);
        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(paths.Assets, "keep.asset"), "asset");
        Directory.CreateDirectory(Path.Combine(paths.Libraries, "example"));
        File.WriteAllText(Path.Combine(paths.Libraries, "example", "keep.jar"), "library");
        Directory.CreateDirectory(Path.Combine(paths.Versions, "1.21.1"));
        File.WriteAllText(Path.Combine(paths.Versions, "1.21.1", "1.21.1.jar"), "client");
        var repository = new JsonInstanceRepository(paths.Instances);
        var manager = new InstanceManager(repository);
        var selected = manager.CreateAsync("A", "1.21.1").GetAwaiter().GetResult();
        var preserved = manager.CreateAsync("B", "1.21.1").GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(repository.GetPaths(selected.Id).Mods, "A.jar"), "A");
        File.WriteAllText(Path.Combine(repository.GetPaths(preserved.Id).Mods, "B.jar"), "B");
        Equal(true, manager.DeleteAsync(selected.Id).GetAwaiter().GetResult());
        Equal(false, Directory.Exists(Path.Combine(paths.Instances, selected.Id.ToString())));
        Equal(true, File.Exists(Path.Combine(repository.GetPaths(preserved.Id).Mods, "B.jar")));
        Equal(true, File.Exists(Path.Combine(paths.Assets, "keep.asset")));
        Equal(true, File.Exists(Path.Combine(paths.Libraries, "example", "keep.jar")));
        Equal(true, File.Exists(Path.Combine(paths.Versions, "1.21.1", "1.21.1.jar")));
    }
    finally { DeleteRoot(root); }
});

Check("Copying a profile creates a new GUID and leaves the source untouched", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var source = manager.CreateAsync("Original", "1.21.1", LoaderType.Fabric, "0.16.14").GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(repository.GetPaths(source.Id).Mods, "mod.jar"), "source");
        File.WriteAllText(Path.Combine(repository.GetPaths(source.Id).Game, "options.txt"), "renderDistance:12");
        var copy = manager.CopyAsync(source.Id, "Copia").GetAwaiter().GetResult();
        Equal(false, source.Id == copy.Id);
        Equal("source", File.ReadAllText(Path.Combine(repository.GetPaths(source.Id).Mods, "mod.jar")));
        Equal("source", File.ReadAllText(Path.Combine(repository.GetPaths(copy.Id).Mods, "mod.jar")));
        File.WriteAllText(Path.Combine(repository.GetPaths(copy.Id).Mods, "mod.jar"), "copy");
        Equal("source", File.ReadAllText(Path.Combine(repository.GetPaths(source.Id).Mods, "mod.jar")));
    }
    finally { DeleteRoot(root); }
});

Check("Legacy readable profile paths migrate to GUID with manifest backup", () =>
{
    var root = TempRoot();
    try
    {
        var legacy = GameInstance.Create("Pack viejo", "1.21.1", LoaderType.Fabric, "0.16.14") with
        {
            DirectoryName = Path.Combine("Fabric", "Pack viejo")
        };
        var oldDirectory = Path.Combine(root, "Fabric", "Pack viejo");
        Directory.CreateDirectory(Path.Combine(oldDirectory, "game", "mods"));
        File.WriteAllText(Path.Combine(oldDirectory, "game", "mods", "keep.jar"), "keep");
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        File.WriteAllText(Path.Combine(oldDirectory, "instance.json"), JsonSerializer.Serialize(legacy, options));
        var repository = new JsonInstanceRepository(root);
        var loaded = repository.GetAsync(legacy.Id).GetAwaiter().GetResult() ?? throw new InvalidOperationException("No se leyó el perfil heredado.");
        repository.SaveAsync(loaded).GetAwaiter().GetResult();
        var canonical = Path.Combine(root, legacy.Id.ToString());
        Equal(canonical, repository.GetInstanceDirectory(legacy.Id));
        Equal(false, Directory.Exists(oldDirectory));
        Equal(true, File.Exists(Path.Combine(canonical, "game", "mods", "keep.jar")));
        Equal(true, Directory.EnumerateFiles(Path.Combine(canonical, "backups"), "instance.layout-v1.*.json").Any());
    }
    finally { DeleteRoot(root); }
});

Check("Corrupt instance manifests are ignored instead of crashing the library", () =>
{
    var root = TempRoot();
    try
    {
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "instance.json"), "{ this is not json");
        var repository = new JsonInstanceRepository(root);
        Equal(0, repository.ListAsync().GetAwaiter().GetResult().Count);
    }
    finally { DeleteRoot(root); }
});

Check("Stale interrupted staging directories are cleaned safely", () =>
{
    var root = TempRoot();
    try
    {
        var stale = Path.Combine(root, ".staging", "abandoned");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "partial.tmp"), "partial");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        _ = new JsonInstanceRepository(root);
        Equal(false, Directory.Exists(stale));
    }
    finally { DeleteRoot(root); }
});

Check("Legacy Minecraft game data is copied non-destructively into a new instance", () =>
{
    var root = TempRoot();
    try
    {
        var versions = Path.Combine(root, "shared", "versions");
        var version = Path.Combine(versions, "1.21.1");
        var legacyGame = Path.Combine(version, "game");
        Directory.CreateDirectory(legacyGame);
        File.WriteAllText(Path.Combine(version, "1.21.1.json"), "{}");
        File.WriteAllText(Path.Combine(version, "1.21.1.jar"), "client");
        File.WriteAllText(Path.Combine(legacyGame, "options.txt"), "legacy=true");
        var repository = new JsonInstanceRepository(Path.Combine(root, "instances"));
        var migrator = new LegacyInstallationMigrator(versions, repository);
        Equal(1, migrator.MigrateAsync().GetAwaiter().GetResult());
        var migrated = repository.ListAsync().GetAwaiter().GetResult().Single();
        Equal("legacy=true", File.ReadAllText(Path.Combine(repository.GetPaths(migrated.Id).Game, "options.txt")));
        Equal("legacy=true", File.ReadAllText(Path.Combine(legacyGame, "options.txt")));
    }
    finally { DeleteRoot(root); }
});

Check("Old shared roots migrate into shared without overwriting data", () =>
{
    var root = TempRoot();
    try
    {
        var oldVersions = Path.Combine(root, "versions", "1.21.8");
        var oldAssets = Path.Combine(root, "assets", "objects", "aa");
        Directory.CreateDirectory(oldVersions);
        Directory.CreateDirectory(oldAssets);
        File.WriteAllText(Path.Combine(oldVersions, "1.21.8.json"), "{}");
        File.WriteAllText(Path.Combine(oldVersions, "1.21.8.jar"), "client");
        File.WriteAllText(Path.Combine(oldAssets, "hash"), "asset");
        var paths = new NexoPaths(root);
        var migrator = new NexoDataLayoutMigrator(paths.Instances, paths.Versions);
        Equal(true, migrator.MigrateSharedVersions() >= 2);
        Equal(true, File.Exists(Path.Combine(paths.Versions, "1.21.8", "1.21.8.jar")));
        Equal(true, File.Exists(Path.Combine(paths.Assets, "objects", "aa", "hash")));
        Equal(false, Directory.Exists(Path.Combine(root, "versions")));
    }
    finally { DeleteRoot(root); }
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

Check("Download planning deduplicates repeated Minecraft assets", () =>
{
    var destination = Path.Combine(Path.GetTempPath(), "nexo-shared-asset");
    var jobs = DownloadJobPlanner.Deduplicate([
        new DownloadJob("https://resources.download.minecraft.net/aa/hash", destination, "hash"),
        new DownloadJob("https://resources.download.minecraft.net/aa/hash", destination, "hash")]);
    Equal(1, jobs.Count);
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

Check("Java Manager parses legacy Java 8 notation", () => Equal(8, JavaRuntimeInspector.ParseMajor("1.8.0_402")));

Check("Java Manager rejects output without a valid version", () =>
    Equal<JavaRuntime?>(null, JavaRuntimeInspector.Parse("not a Java runtime", @"C:\Java\bin\java.exe", "test")));

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
    Equal("47.3.0", forge.LoaderVersion);
    Equal("21.1.200", neoForge.LoaderVersion);
});

Check("Instance editor updates metadata and overrides atomically", () =>
{
    var root = TempRoot();
    try
    {
        var repository = new JsonInstanceRepository(root);
        var manager = new InstanceManager(repository);
        var created = manager.CreateAsync("Original", "1.21.1").GetAwaiter().GetResult();
        var directory = repository.GetInstanceDirectory(created.Id);
        var updated = manager.UpdateAsync(created.Id, "Editada", new InstanceSettings(5120, null, ["-XX:+UseG1GC"], 1280, 720, false)).GetAwaiter().GetResult();
        Equal("Editada", updated.Name);
        Equal(5120, updated.Settings.MemoryMiB);
        Equal(1280, updated.Settings.WindowWidth);
        Equal(directory, repository.GetInstanceDirectory(created.Id));
        Equal(false, File.Exists(Path.Combine(directory, "instance.json.tmp")));
    }
    finally { DeleteRoot(root); }
});

Check("Content Manager installs mods and texture packs inside one instance", () =>
{
    var root = TempRoot();
    try
    {
        var game = Path.Combine(root, "game");
        var mod = Path.Combine(root, "example.jar");
        var texture = Path.Combine(root, "textures.zip");
        File.WriteAllText(mod, "jar");
        using (var zip = ZipFile.Open(texture, ZipArchiveMode.Create))
        {
            zip.CreateEntry("pack.mcmeta");
            zip.CreateEntry("assets/example/texture.png");
        }
        var result = new InstanceContentManager().ImportAsync(game, [mod, texture]).GetAwaiter().GetResult();
        Equal(true, File.Exists(Path.Combine(game, "mods", "example.jar")));
        Equal(true, File.Exists(Path.Combine(game, "resourcepacks", "textures.zip")));
        Equal(2, result.FilesInstalled);
        Equal("jar", File.ReadAllText(mod));
    }
    finally { DeleteRoot(root); }
});

Check("Content Manager imports only physical lcpack overrides", () =>
{
    var root = TempRoot();
    try
    {
        var pack = Path.Combine(root, "example.lcpack");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("metadata.json").Open()))
                writer.Write("{\"mods\":[{\"hash\":\"one\"},{\"hash\":\"two\"}],\"resourcepacks\":[{\"hash\":\"texture\"}]}");
            using (var writer = new StreamWriter(zip.CreateEntry("overrides/mods/included.jar").Open())) writer.Write("jar");
            using (var writer = new StreamWriter(zip.CreateEntry("private/internal.txt").Open())) writer.Write("ignored");
        }
        var game = Path.Combine(root, "game");
        var result = new InstanceContentManager().ImportAsync(game, [pack]).GetAwaiter().GetResult();
        Equal(true, File.Exists(Path.Combine(game, "mods", "included.jar")));
        Equal(false, File.Exists(Path.Combine(game, "private", "internal.txt")));
        Equal(2, result.ReferencedFilesMissing);
    }
    finally { DeleteRoot(root); }
});

Check("Content Manager rejects packs for a different Minecraft version", () =>
{
    var root = TempRoot();
    try
    {
        var pack = Path.Combine(root, "fabric-1211.lcpack");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("metadata.json").Open()))
                writer.Write("{\"gameVersion\":\"1.21.1\",\"loaders\":[\"fabric\"]}");
            using (var writer = new StreamWriter(zip.CreateEntry("overrides/mods/example.jar").Open())) writer.Write("jar");
        }
        var rejected = false;
        try { new InstanceContentManager().ImportAsync(Path.Combine(root, "game"), [pack], "1.21.8", "fabric").GetAwaiter().GetResult(); }
        catch (InvalidDataException exception) { rejected = exception.Message.Contains("1.21.1", StringComparison.Ordinal); }
        Equal(true, rejected);
        Equal(false, File.Exists(Path.Combine(root, "game", "mods", "example.jar")));
    }
    finally { DeleteRoot(root); }
});

Check("Content Manager blocks archive path traversal", () =>
{
    var root = TempRoot();
    try
    {
        var pack = Path.Combine(root, "unsafe.lcpack");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("overrides/mods/../../../escape.jar").Open())) writer.Write("bad");
        var blocked = false;
        try { new InstanceContentManager().ImportAsync(Path.Combine(root, "game"), [pack]).GetAwaiter().GetResult(); }
        catch (InvalidDataException) { blocked = true; }
        Equal(true, blocked);
        Equal(false, File.Exists(Path.Combine(root, "escape.jar")));
    }
    finally { DeleteRoot(root); }
});

Check("Official mrpack downloads remote files and verifies SHA-512", () =>
{
    var root = TempRoot();
    try
    {
        var bytes = Encoding.UTF8.GetBytes("remote-mod");
        var sha512 = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
        var pack = Path.Combine(root, "example.mrpack");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
                writer.Write($$"""{"formatVersion":1,"game":"minecraft","versionId":"1","name":"Example","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16.14"},"files":[{"path":"mods/remote.jar","hashes":{"sha512":"{{sha512}}"},"env":{"client":"required","server":"required"},"downloads":["https://cdn.example/remote.jar"]}]}""");
            using (var writer = new StreamWriter(zip.CreateEntry("overrides/config/example.json").Open())) writer.Write("{}");
        }
        using var http = new HttpClient(new StaticHandler(bytes));
        var result = new InstanceContentManager(http).ImportAsync(Path.Combine(root, "game"), [pack], "1.21.1", "fabric").GetAwaiter().GetResult();
        Equal(2, result.FilesInstalled);
        Equal(0, result.ReferencedFilesMissing);
        Equal("remote-mod", File.ReadAllText(Path.Combine(root, "game", "mods", "remote.jar")));
        Equal(true, File.Exists(Path.Combine(root, "game", "config", "example.json")));
    }
    finally { DeleteRoot(root); }
});

Check("Official mrpack rejects a bad remote hash", () =>
{
    var root = TempRoot();
    try
    {
        var pack = Path.Combine(root, "bad.mrpack");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write("{\"formatVersion\":1,\"game\":\"minecraft\",\"versionId\":\"1\",\"dependencies\":{\"minecraft\":\"1.21.1\"},\"files\":[{\"path\":\"mods/bad.jar\",\"hashes\":{\"sha512\":\"00\"},\"downloads\":[\"https://cdn.example/bad.jar\"]}]}");
        using var http = new HttpClient(new StaticHandler(Encoding.UTF8.GetBytes("wrong")));
        var rejected = false;
        try { new InstanceContentManager(http).ImportAsync(Path.Combine(root, "game"), [pack], "1.21.1", "vanilla").GetAwaiter().GetResult(); }
        catch (InvalidOperationException) { rejected = true; }
        Equal(true, rejected);
        Equal(false, File.Exists(Path.Combine(root, "game", "mods", "bad.jar")));
    }
    finally { DeleteRoot(root); }
});

Check("CurseForge importer recognizes official exported profiles", () =>
{
    var root = TempRoot();
    try
    {
        var pack = Path.Combine(root, "profile.zip");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
                writer.Write("{\"name\":\"Example\",\"minecraft\":{\"version\":\"1.21.1\",\"modLoaders\":[{\"id\":\"fabric-0.16.14\",\"primary\":true}]},\"files\":[],\"overrides\":\"overrides\"}");
            using (var writer = new StreamWriter(zip.CreateEntry("overrides/config/example.json").Open())) writer.Write("{}");
        }
        Equal(true, CurseForgePackInstaller.IsPack(pack));
        Equal(false, CurseForgePackInstaller.IsPack(Path.Combine(root, "missing.zip")));
        using var http = new HttpClient(new StaticHandler(Array.Empty<byte>()));
        var installer = new CurseForgePackInstaller(http);
        var result = installer.InstallAsync(pack, Path.Combine(root, "game"), "1.21.1", "fabric").GetAwaiter().GetResult();
        Equal(0, result.FilesDownloaded);
        Equal(1, result.OverridesInstalled);
    }
    finally { DeleteRoot(root); }
});

Check("CurseForge remote exports require a developer API key rather than a user login", () =>
{
    var root = TempRoot();
    try
    {
        var pack = Path.Combine(root, "remote.zip");
        using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write("{\"name\":\"Remote\",\"minecraft\":{\"version\":\"1.21.1\",\"modLoaders\":[{\"id\":\"fabric-0.16.14\",\"primary\":true}]},\"files\":[{\"projectID\":1,\"fileID\":2,\"required\":true}],\"overrides\":\"overrides\"}");
        using var http = new HttpClient(new StaticHandler(Array.Empty<byte>()));
        var installer = new CurseForgePackInstaller(http, apiKey: null);
        if (Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY") is not null) return;
        var rejected = false;
        try { installer.InstallAsync(pack, Path.Combine(root, "game"), "1.21.1", "fabric").GetAwaiter().GetResult(); }
        catch (InvalidOperationException exception) { rejected = exception.Message.Contains("desarrollador", StringComparison.OrdinalIgnoreCase); }
        Equal(true, rejected);
    }
    finally { DeleteRoot(root); }
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    Console.Error.WriteLine($"{passed} checks passed; {failures.Count} failed.");
    return 1;
}

Console.WriteLine($"{passed} checks passed.");
return 0;

void Check(string name, Action action)
{
    try
    {
        action();
        passed++;
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
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static string TempRoot()
{
    var root = Path.Combine(Path.GetTempPath(), "nexo-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}

static void DeleteRoot(string root)
{
    try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

sealed class StaticHandler(byte[] payload) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(payload)
        };
        return Task.FromResult(response);
    }
}
