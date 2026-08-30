using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NexoLauncher.Infrastructure.Content;

public enum NexoBoostPreset
{
    Balanced
}

public sealed record NexoBoostPresetResult(
    NexoBoostPreset Preset,
    IReadOnlyList<string> Changes,
    bool ParticleCoreConfigured);

public sealed record NexoBoostPresetRestoreResult(
    int ValuesRestored,
    IReadOnlyList<string> PreservedValues);

/// <summary>
/// Ajusta únicamente opciones visuales con una relación coste/beneficio clara.
/// El preset equilibrado conserva partículas de combate, AO, sombras, nubes,
/// mipmaps y calidad gráfica; limita distancias excesivas y, si Particle Core
/// ya generó su configuración, reduce sólo partículas ambientales prescindibles.
/// Todo cambio administrado queda registrado para poder restaurarlo sin pisar
/// modificaciones posteriores del usuario.
/// </summary>
public sealed class NexoBoostPresetService
{
    private const int ManifestSchema = 1;
    private const string ManifestName = "nexo-boost-preset.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<NexoBoostPresetResult> ApplyAsync(
        string gameDirectory,
        NexoBoostPreset preset = NexoBoostPreset.Balanced,
        CancellationToken token = default)
    {
        if (preset != NexoBoostPreset.Balanced)
            throw new NotSupportedException("Este preset de NEXO Boost todavía no está implementado.");

        gameDirectory = NormalizeGameDirectory(gameDirectory);
        Directory.CreateDirectory(gameDirectory);
        var manifest = await LoadManifestAsync(gameDirectory, token)
                       ?? new PresetManifest(ManifestSchema, preset, DateTimeOffset.UtcNow, [], []);
        if (manifest.SchemaVersion != ManifestSchema)
            throw new InvalidDataException("El manifiesto de configuración de NEXO Boost no es compatible.");

        var changes = new List<string>();
        var optionState = manifest.Options?.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase)
                          ?? new Dictionary<string, OptionState>(StringComparer.OrdinalIgnoreCase);
        await ApplyBalancedOptionsAsync(gameDirectory, optionState, changes, token);

        var configStates = manifest.Configs?.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                           ?? new Dictionary<string, JsonConfigState>(StringComparer.OrdinalIgnoreCase);
        var particleCoreConfigured = await ApplyParticleCoreAsync(gameDirectory, configStates, changes, token);

        var updated = new PresetManifest(
            ManifestSchema,
            preset,
            DateTimeOffset.UtcNow,
            optionState.Values.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray(),
            configStates.Values.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
        await WriteManifestAsync(gameDirectory, updated, token);

        if (!particleCoreConfigured)
            changes.Add("Particle Core: se conservará su optimización por defecto; vuelve a aplicar Equilibrado después del primer inicio para afinar goteos y ambiente.");

        return new NexoBoostPresetResult(preset, changes, particleCoreConfigured);
    }

    public async Task<NexoBoostPresetRestoreResult> RestoreAsync(
        string gameDirectory,
        CancellationToken token = default)
    {
        gameDirectory = NormalizeGameDirectory(gameDirectory);
        var path = ManifestPath(gameDirectory, ensureRuntime: false);
        if (!File.Exists(path)) return new NexoBoostPresetRestoreResult(0, []);

        var manifest = await LoadManifestAsync(gameDirectory, token)
                       ?? throw new InvalidDataException("El manifiesto de configuración de NEXO Boost está vacío.");
        if (manifest.SchemaVersion != ManifestSchema)
            throw new InvalidDataException("El manifiesto de configuración de NEXO Boost no es compatible; no se restaurarán valores a ciegas.");

        var restored = 0;
        var preserved = new List<string>();

        var optionsPath = Path.Combine(gameDirectory, "options.txt");
        if (File.Exists(optionsPath) && manifest.Options is { Count: > 0 })
        {
            var options = await OptionsDocument.LoadAsync(optionsPath, token);
            foreach (var state in manifest.Options)
            {
                var current = options.Get(state.Key);
                if (!string.Equals(current, state.AppliedValue, StringComparison.Ordinal))
                {
                    preserved.Add($"options.txt:{state.Key} (cambiado después de aplicar Boost)");
                    continue;
                }

                if (state.OriginalValue is null) options.Remove(state.Key);
                else options.Set(state.Key, state.OriginalValue);
                restored++;
            }
            await options.SaveAsync(optionsPath, token);
        }

        if (manifest.Configs is not null)
        {
            foreach (var config in manifest.Configs)
            {
                token.ThrowIfCancellationRequested();
                var configPath = SafeGameFile(gameDirectory, config.RelativePath);
                if (!File.Exists(configPath)) continue;

                JsonObject root;
                try { root = await LoadJsonObjectAsync(configPath, token); }
                catch (JsonException)
                {
                    preserved.Add(config.RelativePath + " (JSON modificado/no interpretable)");
                    continue;
                }

                var changed = false;
                foreach (var state in config.Values)
                {
                    var current = GetJsonPath(root, state.Path)?.ToJsonString();
                    if (!string.Equals(current, state.AppliedJson, StringComparison.Ordinal))
                    {
                        preserved.Add(config.RelativePath + ":" + state.Path + " (cambiado después de aplicar Boost)");
                        continue;
                    }

                    SetJsonPath(root, state.Path, state.OriginalJson is null ? null : JsonNode.Parse(state.OriginalJson));
                    changed = true;
                    restored++;
                }

                if (changed) await WriteJsonObjectAsync(configPath, root, token);
            }
        }

        File.Delete(path);
        return new NexoBoostPresetRestoreResult(restored, preserved);
    }

    private static async Task ApplyBalancedOptionsAsync(
        string gameDirectory,
        IDictionary<string, OptionState> states,
        ICollection<string> changes,
        CancellationToken token)
    {
        var path = Path.Combine(gameDirectory, "options.txt");
        var options = await OptionsDocument.LoadAsync(path, token);

        Apply("particles", "0", "Partículas: TODAS (combate intacto)");
        ApplyIntCap("renderDistance", 12, "Distancia de renderizado");
        ApplyIntCap("simulationDistance", 8, "Distancia de simulación");
        ApplyDoubleCap("entityDistanceScaling", 0.85, "Distancia de entidades");
        ApplyIntCap("biomeBlendRadius", 2, "Mezcla de biomas");

        await options.SaveAsync(path, token);
        return;

        void Apply(string key, string applied, string description)
        {
            var current = options.Get(key);
            var original = states.TryGetValue(key, out var existing) ? existing.OriginalValue : current;
            options.Set(key, applied);
            states[key] = new OptionState(key, original, applied);
            if (!string.Equals(current, applied, StringComparison.Ordinal)) changes.Add($"{description}: {current ?? "predeterminado"} → {applied}");
        }

        void ApplyIntCap(string key, int cap, string description)
        {
            var currentText = options.Get(key);
            var current = int.TryParse(currentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : cap;
            Apply(key, Math.Min(current, cap).ToString(CultureInfo.InvariantCulture), description);
        }

        void ApplyDoubleCap(string key, double cap, string description)
        {
            var currentText = options.Get(key);
            var current = double.TryParse(currentText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : cap;
            Apply(key, Math.Min(current, cap).ToString("0.##", CultureInfo.InvariantCulture), description);
        }
    }

    private static async Task<bool> ApplyParticleCoreAsync(
        string gameDirectory,
        IDictionary<string, JsonConfigState> configs,
        ICollection<string> changes,
        CancellationToken token)
    {
        var configDirectory = Path.Combine(gameDirectory, "config");
        if (!Directory.Exists(configDirectory)) return false;
        if (new DirectoryInfo(configDirectory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("config/ no puede ser un enlace o junction al aplicar NEXO Boost.");

        var files = Directory.EnumerateFiles(configDirectory, "particle_core_config_v*.json", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0) return false;

        var configured = false;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            JsonObject root;
            try { root = await LoadJsonObjectAsync(file, token); }
            catch (JsonException)
            {
                changes.Add($"Particle Core: {Path.GetFileName(file)} no se modificó porque su JSON no se pudo interpretar.");
                continue;
            }

            var relative = Path.GetRelativePath(gameDirectory, file).Replace('\\', '/');
            var prior = configs.TryGetValue(relative, out var existing)
                ? existing.Values.ToDictionary(item => item.Path, StringComparer.Ordinal)
                : new Dictionary<string, JsonValueState>(StringComparer.Ordinal);
            var values = new Dictionary<string, JsonValueState>(prior, StringComparer.Ordinal);

            Set("turnOffPotionParticles", JsonValue.Create(false));
            Set("disableParticles", JsonValue.Create(false));
            Set("reduceParticlesAllChance", JsonValue.Create(1.0));
            Set("reduceParticlesDecreasedChance", JsonValue.Create(1.0));

            // Ambiente de bajo valor visual: se reduce, no se elimina de forma global.
            Set("reduceParticlesByType/minecraft:dripping_water", JsonValue.Create(0.15));
            Set("reduceParticlesByType/minecraft:falling_water", JsonValue.Create(0.15));
            Set("reduceParticlesByType/minecraft:landing_water", JsonValue.Create(0.15));
            Set("reduceParticlesByType/minecraft:dripping_lava", JsonValue.Create(0.25));
            Set("reduceParticlesByType/minecraft:falling_lava", JsonValue.Create(0.25));
            Set("reduceParticlesByType/minecraft:landing_lava", JsonValue.Create(0.25));
            Set("reduceParticlesByType/minecraft:rain", JsonValue.Create(0.35));
            Set("reduceParticlesByType/minecraft:underwater", JsonValue.Create(0.35));
            Set("reduceParticlesByType/minecraft:ash", JsonValue.Create(0.40));
            Set("reduceParticlesByType/minecraft:white_ash", JsonValue.Create(0.40));
            Set("reduceParticlesByType/minecraft:crimson_spore", JsonValue.Create(0.45));
            Set("reduceParticlesByType/minecraft:warped_spore", JsonValue.Create(0.45));
            Set("reduceParticlesByType/minecraft:spore_blossom_air", JsonValue.Create(0.35));
            Set("reduceParticlesByType/minecraft:mycelium", JsonValue.Create(0.50));
            Set("reduceParticlesByType/minecraft:cloud", JsonValue.Create(0.60));

            // Señales de combate y gameplay: siempre completas en Equilibrado.
            Set("reduceParticlesByType/minecraft:sweep_attack", JsonValue.Create(1.0));
            Set("reduceParticlesByType/minecraft:damage_indicator", JsonValue.Create(1.0));
            Set("reduceParticlesByType/minecraft:crit", JsonValue.Create(1.0));
            Set("reduceParticlesByType/minecraft:enchanted_hit", JsonValue.Create(1.0));
            Set("reduceParticlesByType/minecraft:totem_of_undying", JsonValue.Create(1.0));
            Set("reduceParticlesByType/minecraft:heart", JsonValue.Create(1.0));

            configs[relative] = new JsonConfigState(relative, values.Values.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray());
            await WriteJsonObjectAsync(file, root, token);
            configured = true;
            changes.Add("Particle Core: ambiente reducido selectivamente; partículas de combate preservadas al 100%.");

            void Set(string path, JsonNode? appliedNode)
            {
                var currentNode = GetJsonPath(root, path);
                var currentJson = currentNode?.ToJsonString();
                var appliedJson = appliedNode?.ToJsonString() ?? "null";
                var original = prior.TryGetValue(path, out var old) ? old.OriginalJson : currentJson;
                SetJsonPath(root, path, appliedNode?.DeepClone());
                values[path] = new JsonValueState(path, original, appliedJson);
            }
        }

        return configured;
    }

    private static JsonNode? GetJsonPath(JsonObject root, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = root;
        foreach (var part in parts)
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(part, out current)) return null;
        }
        return current;
    }

    private static void SetJsonPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new InvalidDataException("Ruta JSON vacía.");
        var current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (current[parts[index]] is not JsonObject child)
            {
                child = new JsonObject();
                current[parts[index]] = child;
            }
            current = child;
        }

        if (value is null) current.Remove(parts[^1]);
        else current[parts[^1]] = value;
    }

    private static async Task<JsonObject> LoadJsonObjectAsync(string path, CancellationToken token)
    {
        var text = await File.ReadAllTextAsync(path, token);
        var node = JsonNode.Parse(text, null, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return node as JsonObject ?? throw new JsonException("La configuración no contiene un objeto JSON raíz.");
    }

    private static Task WriteJsonObjectAsync(string path, JsonObject root, CancellationToken token) =>
        WriteAtomicTextAsync(path, root.ToJsonString(Json), token);

    private static async Task<PresetManifest?> LoadManifestAsync(string gameDirectory, CancellationToken token)
    {
        var path = ManifestPath(gameDirectory, ensureRuntime: false);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<PresetManifest>(await File.ReadAllBytesAsync(path, token), Json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("El manifiesto de configuración de NEXO Boost está dañado.", exception);
        }
    }

    private static Task WriteManifestAsync(string gameDirectory, PresetManifest manifest, CancellationToken token) =>
        WriteAtomicBytesAsync(ManifestPath(gameDirectory, ensureRuntime: true), JsonSerializer.SerializeToUtf8Bytes(manifest, Json), token);

    private static async Task WriteAtomicTextAsync(string path, string text, CancellationToken token) =>
        await WriteAtomicBytesAsync(path, new UTF8Encoding(false).GetBytes(text), token);

    private static async Task WriteAtomicBytesAsync(string path, byte[] bytes, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".nexo.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough))
                stream.Flush(flushToDisk: true);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ManifestPath(string gameDirectory, bool ensureRuntime)
    {
        var game = NormalizeGameDirectory(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var instanceRoot = Directory.GetParent(game)?.FullName
                           ?? throw new InvalidOperationException("No se pudo resolver la raíz de la instancia.");
        var runtime = Path.Combine(instanceRoot, "runtime");
        if (Directory.Exists(runtime) && new DirectoryInfo(runtime).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("runtime/ no puede ser un enlace o junction.");
        if (ensureRuntime) Directory.CreateDirectory(runtime);
        return Path.Combine(runtime, ManifestName);
    }

    private static string SafeGameFile(string gameDirectory, string relativePath)
    {
        var root = NormalizeGameDirectory(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalized = relativePath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part is "." or ".." || part.Contains(':')))
            throw new InvalidDataException("El preset de Boost contiene una ruta no válida.");
        var candidate = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("El preset de Boost intenta salir del gameDirectory.");
        ContentImportTransaction.EnsurePhysicalDestination(root, candidate);
        return candidate;
    }

    private static string NormalizeGameDirectory(string gameDirectory) =>
        Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed record OptionState(string Key, string? OriginalValue, string AppliedValue);
    private sealed record JsonValueState(string Path, string? OriginalJson, string AppliedJson);
    private sealed record JsonConfigState(string RelativePath, IReadOnlyList<JsonValueState> Values);
    private sealed record PresetManifest(
        int SchemaVersion,
        NexoBoostPreset Preset,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<OptionState>? Options,
        IReadOnlyList<JsonConfigState>? Configs);

    private sealed class OptionsDocument(List<string> lines)
    {
        private readonly List<string> lines = lines;

        public static async Task<OptionsDocument> LoadAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new OptionsDocument([]);
            var text = await File.ReadAllTextAsync(path, token);
            return new OptionsDocument(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Where(line => line.Length > 0).ToList());
        }

        public string? Get(string key)
        {
            var prefix = key + ":";
            var line = lines.LastOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            return line is null ? null : line[prefix.Length..];
        }

        public void Set(string key, string value)
        {
            Remove(key);
            lines.Add(key + ":" + value);
        }

        public void Remove(string key)
        {
            var prefix = key + ":";
            lines.RemoveAll(value => value.StartsWith(prefix, StringComparison.Ordinal));
        }

        public Task SaveAsync(string path, CancellationToken token) =>
            WriteAtomicTextAsync(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, token);
    }
}
