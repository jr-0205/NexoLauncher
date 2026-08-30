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
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;

/**
 * Coordinación opcional con Sodium Extra. NEXA modifica únicamente opciones de
 * coste ambiental que no cambian niebla protegida, VSync, resolución, shaders
 * ni otras reglas sensibles de gameplay. El resto del JSON se conserva.
 */
public final class NexaSodiumExtraTuner {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    private static final String MOD_ID = "sodium-extra";
    private static final String FILE_NAME = "sodium-extra-options.json";

    private NexaSodiumExtraTuner() { }

    /**
     * @return true cuando no hay nada que sincronizar o la configuración ya se
     *         escribió; false si Sodium Extra está instalado pero todavía no creó
     *         su archivo y NEXA debe reintentar en un tick posterior.
     */
    public static boolean apply(NexoPerformancePreset preset) {
        if (preset == null || !FabricLoader.getInstance().isModLoaded(MOD_ID)) return true;

        Path path = FabricLoader.getInstance().getConfigDir().resolve(FILE_NAME);
        if (!Files.isRegularFile(path)) return false;

        try {
            JsonObject root;
            try (Reader reader = Files.newBufferedReader(path, StandardCharsets.UTF_8)) {
                JsonElement parsed = JsonParser.parseReader(reader);
                if (!parsed.isJsonObject()) return true;
                root = parsed.getAsJsonObject();
            }

            JsonObject particles = object(root, "particle_settings");
            JsonObject detail = object(root, "detail_settings");
            JsonObject extra = object(root, "extra_settings");

            switch (preset) {
                case LOW -> {
                    particles.addProperty("rain_splash", false);
                    detail.addProperty("rain_snow", false);
                    extra.addProperty("cloud_distance", 32);
                    extra.addProperty("steady_debug_hud_refresh_interval", 5);
                }
                case MEDIUM_LOW -> {
                    particles.addProperty("rain_splash", false);
                    detail.addProperty("rain_snow", true);
                    extra.addProperty("cloud_distance", 48);
                    extra.addProperty("steady_debug_hud_refresh_interval", 4);
                }
                case MEDIUM -> {
                    particles.addProperty("rain_splash", false);
                    detail.addProperty("rain_snow", true);
                    extra.addProperty("cloud_distance", 64);
                    extra.addProperty("steady_debug_hud_refresh_interval", 3);
                }
                case MEDIUM_HIGH -> {
                    particles.addProperty("rain_splash", true);
                    detail.addProperty("rain_snow", true);
                    extra.addProperty("cloud_distance", 80);
                    extra.addProperty("steady_debug_hud_refresh_interval", 2);
                }
                case HIGH -> {
                    particles.addProperty("rain_splash", true);
                    detail.addProperty("rain_snow", true);
                    extra.addProperty("cloud_distance", 100);
                    extra.addProperty("steady_debug_hud_refresh_interval", 1);
                }
            }

            writeAtomic(path, root);
            return true;
        }
        catch (IOException | RuntimeException ignored) {
            // Sodium Extra es opcional: un fallo suyo nunca debe romper NEXA In-Game.
            return true;
        }
    }

    private static JsonObject object(JsonObject root, String key) {
        JsonElement current = root.get(key);
        if (current != null && current.isJsonObject()) return current.getAsJsonObject();
        JsonObject created = new JsonObject();
        root.add(key, created);
        return created;
    }

    private static void writeAtomic(Path path, JsonObject root) throws IOException {
        Path temporary = path.resolveSibling(path.getFileName() + ".nexa.tmp");
        try {
            try (Writer writer = Files.newBufferedWriter(temporary, StandardCharsets.UTF_8)) {
                GSON.toJson(root, writer);
            }
            try {
                Files.move(temporary, path, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
            }
            catch (java.nio.file.AtomicMoveNotSupportedException ignored) {
                Files.move(temporary, path, StandardCopyOption.REPLACE_EXISTING);
            }
        }
        finally {
            Files.deleteIfExists(temporary);
        }
    }
}
