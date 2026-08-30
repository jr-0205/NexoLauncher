package com.nexoclient.ingame.performance;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.io.Reader;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.Path;

final class NexoParticleTuner {
    private static final Gson JSON = new GsonBuilder().setPrettyPrinting().create();

    private NexoParticleTuner() { }

    static void apply(NexoPerformancePreset preset) {
        Path configDir = FabricLoader.getInstance().getConfigDir();
        if (!Files.isDirectory(configDir)) return;

        try (DirectoryStream<Path> files = Files.newDirectoryStream(configDir, "particle_core_config_v*.json")) {
            for (Path file : files) patch(file, preset);
        }
        catch (IOException ignored) {
            // Particle Core is optional; NEXO presets still apply vanilla settings.
        }
    }

    private static void patch(Path file, NexoPerformancePreset preset) {
        JsonObject root;
        try (Reader reader = Files.newBufferedReader(file, StandardCharsets.UTF_8)) {
            JsonElement element = JsonParser.parseReader(reader);
            if (!element.isJsonObject()) return;
            root = element.getAsJsonObject();
        }
        catch (Exception ignored) {
            return;
        }

        root.addProperty("turnOffPotionParticles", false);
        root.addProperty("disableParticles", false);
        root.addProperty("reduceParticlesAllChance", 1.0d);
        root.addProperty("reduceParticlesDecreasedChance", 1.0d);

        JsonObject byType;
        JsonElement current = root.get("reduceParticlesByType");
        if (current != null && current.isJsonObject()) byType = current.getAsJsonObject();
        else {
            byType = new JsonObject();
            root.add("reduceParticlesByType", byType);
        }

        AmbientProfile ambient = AmbientProfile.forPreset(preset);
        set(byType, "minecraft:dripping_water", ambient.water);
        set(byType, "minecraft:falling_water", ambient.water);
        set(byType, "minecraft:landing_water", ambient.water);
        set(byType, "minecraft:dripping_lava", ambient.lava);
        set(byType, "minecraft:falling_lava", ambient.lava);
        set(byType, "minecraft:landing_lava", ambient.lava);
        set(byType, "minecraft:rain", ambient.rain);
        set(byType, "minecraft:underwater", ambient.underwater);
        set(byType, "minecraft:ash", ambient.ash);
        set(byType, "minecraft:white_ash", ambient.ash);
        set(byType, "minecraft:crimson_spore", ambient.spores);
        set(byType, "minecraft:warped_spore", ambient.spores);
        set(byType, "minecraft:spore_blossom_air", ambient.blossom);
        set(byType, "minecraft:mycelium", ambient.mycelium);
        set(byType, "minecraft:cloud", ambient.cloud);

        // Gameplay/combat feedback is never reduced by NEXO presets.
        set(byType, "minecraft:sweep_attack", 1.0d);
        set(byType, "minecraft:damage_indicator", 1.0d);
        set(byType, "minecraft:crit", 1.0d);
        set(byType, "minecraft:enchanted_hit", 1.0d);
        set(byType, "minecraft:totem_of_undying", 1.0d);
        set(byType, "minecraft:heart", 1.0d);

        Path temporary = file.resolveSibling(file.getFileName() + ".nexo.tmp");
        try (Writer writer = Files.newBufferedWriter(temporary, StandardCharsets.UTF_8)) {
            JSON.toJson(root, writer);
        }
        catch (IOException ignored) {
            return;
        }

        try {
            try {
                Files.move(temporary, file,
                    java.nio.file.StandardCopyOption.REPLACE_EXISTING,
                    java.nio.file.StandardCopyOption.ATOMIC_MOVE);
            }
            catch (java.nio.file.AtomicMoveNotSupportedException ignored) {
                Files.move(temporary, file, java.nio.file.StandardCopyOption.REPLACE_EXISTING);
            }
        }
        catch (IOException ignored) {
            try { Files.deleteIfExists(temporary); } catch (IOException ignoredAgain) { }
        }
    }

    private static void set(JsonObject object, String key, double value) {
        object.addProperty(key, value);
    }

    private record AmbientProfile(
        double water,
        double lava,
        double rain,
        double underwater,
        double ash,
        double spores,
        double blossom,
        double mycelium,
        double cloud) {

        static AmbientProfile forPreset(NexoPerformancePreset preset) {
            return switch (preset) {
                case MAX_FPS -> new AmbientProfile(0.05, 0.10, 0.15, 0.18, 0.20, 0.20, 0.15, 0.25, 0.30);
                case MEDIUM -> new AmbientProfile(0.15, 0.25, 0.35, 0.35, 0.40, 0.45, 0.35, 0.50, 0.60);
                case MEDIUM_HIGH -> new AmbientProfile(0.35, 0.45, 0.60, 0.60, 0.65, 0.70, 0.60, 0.75, 0.80);
                case HIGH -> new AmbientProfile(0.65, 0.70, 0.82, 0.82, 0.88, 0.90, 0.85, 0.92, 0.95);
            };
        }
    }
}
