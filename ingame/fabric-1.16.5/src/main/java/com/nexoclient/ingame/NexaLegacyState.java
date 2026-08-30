package com.nexoclient.ingame;

import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.ParticlesMode;

import java.io.IOException;
import java.io.Reader;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Properties;

final class NexaLegacyState {
    enum Preset {
        LOW("Bajo", 6, 0.55f, ParticlesMode.MINIMAL),
        MEDIUM_LOW("Medio-bajo", 8, 0.68f, ParticlesMode.DECREASED),
        MEDIUM("Medio", 10, 0.82f, ParticlesMode.ALL),
        MEDIUM_HIGH("Medio-alto", 12, 0.92f, ParticlesMode.ALL),
        HIGH("Alto", 16, 1.00f, ParticlesMode.ALL);

        final String label;
        final int renderDistance;
        final float entityDistance;
        final ParticlesMode particles;
        Preset(String label, int renderDistance, float entityDistance, ParticlesMode particles) {
            this.label = label; this.renderDistance = renderDistance; this.entityDistance = entityDistance; this.particles = particles;
        }
    }

    private final Path config = FabricLoader.getInstance().getConfigDir().resolve("nexa-ingame.properties");
    private Preset preset = Preset.MEDIUM;
    private boolean fps = true;
    private boolean coordinates = true;
    private boolean pending = true;

    NexaLegacyState() { load(); }
    Preset preset() { return preset; }
    boolean fps() { return fps; }
    boolean coordinates() { return coordinates; }

    void setPreset(Preset value) { preset = value; pending = true; save(); }
    void toggleFps() { fps = !fps; save(); }
    void toggleCoordinates() { coordinates = !coordinates; save(); }

    void applyIfPending(MinecraftClient client) {
        if (!pending || client == null || client.options == null) return;
        client.options.viewDistance = preset.renderDistance;
        client.options.entityDistanceScaling = preset.entityDistance;
        client.options.particles = preset.particles;
        client.options.write();
        pending = false;
    }

    private void load() {
        if (!Files.isRegularFile(config)) return;
        Properties p = new Properties();
        try (Reader reader = Files.newBufferedReader(config, StandardCharsets.UTF_8)) {
            p.load(reader);
            String raw = p.getProperty("performancePreset", "MEDIUM");
            if ("MAX_FPS".equalsIgnoreCase(raw)) raw = "LOW";
            preset = Preset.valueOf(raw);
            fps = Boolean.parseBoolean(p.getProperty("fps", "true"));
            coordinates = Boolean.parseBoolean(p.getProperty("coordinates", "true"));
        } catch (IOException | IllegalArgumentException ignored) { preset = Preset.MEDIUM; }
    }

    private void save() {
        Properties p = new Properties();
        p.setProperty("performancePreset", preset.name());
        p.setProperty("fps", Boolean.toString(fps));
        p.setProperty("coordinates", Boolean.toString(coordinates));
        try {
            Files.createDirectories(config.getParent());
            Path temp = config.resolveSibling(config.getFileName().toString() + ".tmp");
            try (Writer writer = Files.newBufferedWriter(temp, StandardCharsets.UTF_8)) { p.store(writer, "NEXA In-Game settings"); }
            try { Files.move(temp, config, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE); }
            catch (java.nio.file.AtomicMoveNotSupportedException ignored) { Files.move(temp, config, StandardCopyOption.REPLACE_EXISTING); }
        } catch (IOException ignored) { }
    }
}
