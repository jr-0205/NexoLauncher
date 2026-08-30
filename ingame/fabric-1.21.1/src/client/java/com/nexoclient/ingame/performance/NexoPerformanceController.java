package com.nexoclient.ingame.performance;

import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.SimpleOption;

import java.io.IOException;
import java.io.Reader;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

public final class NexoPerformanceController {
    private static final String PRESET_KEY = "performancePreset";
    private final Path configPath;
    private NexoPerformancePreset selected;
    private boolean applyPending;
    private boolean sodiumExtraPending;

    public NexoPerformanceController() {
        configPath = FabricLoader.getInstance().getConfigDir().resolve("nexo-ingame.properties");
        selected = load();
        // Seguridad: nunca tocar opciones de Minecraft durante el arranque.
        // El preset guardado sólo se aplica cuando el usuario lo selecciona explícitamente.
        applyPending = false;
        sodiumExtraPending = false;
    }

    public NexoPerformancePreset selected() {
        return selected;
    }

    public void select(NexoPerformancePreset preset) {
        if (preset == null) return;
        selected = preset;
        applyPending = true;
        sodiumExtraPending = true;
        save();
    }

    public void applyIfPending(MinecraftClient client) {
        if (client == null || client.options == null) return;
        if (applyPending) {
            applyMinecraft(client, selected);
            applyPending = false;
        }
        if (sodiumExtraPending) sodiumExtraPending = !NexaSodiumExtraTuner.apply(selected);
    }

    public void applyNow(MinecraftClient client, NexoPerformancePreset preset) {
        select(preset);
        applyIfPending(client);
    }

    private static void applyMinecraft(MinecraftClient client, NexoPerformancePreset preset) {
        client.options.getViewDistance().setValue(preset.renderDistance());
        client.options.getSimulationDistance().setValue(preset.simulationDistance());
        client.options.getEntityDistanceScaling().setValue(preset.entityDistanceScaling());
        applyParticleMode(client, preset);
        NexoParticleTuner.apply(preset);
    }

    @SuppressWarnings({"rawtypes", "unchecked"})
    private static void applyParticleMode(MinecraftClient client, NexoPerformancePreset preset) {
        SimpleOption option = client.options.getParticles();
        Object current = option.getValue();
        if (!(current instanceof Enum<?> currentEnum)) return;

        try {
            Class enumType = currentEnum.getDeclaringClass();
            Object resolved = Enum.valueOf(enumType, preset.particleMode().name());
            option.setValue(resolved);
        }
        catch (IllegalArgumentException ignored) {
            // Si una versión futura renombra estos valores, no se modifica esta opción.
        }
    }

    private NexoPerformancePreset load() {
        if (!Files.isRegularFile(configPath)) return NexoPerformancePreset.MEDIUM;

        var properties = new Properties();
        try (Reader reader = Files.newBufferedReader(configPath, StandardCharsets.UTF_8)) {
            properties.load(reader);
            var raw = properties.getProperty(PRESET_KEY, NexoPerformancePreset.MEDIUM.name());
            if ("MAX_FPS".equalsIgnoreCase(raw)) return NexoPerformancePreset.LOW;
            return NexoPerformancePreset.valueOf(raw);
        }
        catch (IOException | IllegalArgumentException ignored) {
            return NexoPerformancePreset.MEDIUM;
        }
    }

    private void save() {
        var properties = new Properties();
        properties.setProperty(PRESET_KEY, selected.name());
        try {
            Files.createDirectories(configPath.getParent());
            var temporary = configPath.resolveSibling(configPath.getFileName() + ".tmp");
            try (Writer writer = Files.newBufferedWriter(temporary, StandardCharsets.UTF_8)) {
                properties.store(writer, "NEXA In-Game settings");
            }
            try {
                Files.move(temporary, configPath,
                    java.nio.file.StandardCopyOption.REPLACE_EXISTING,
                    java.nio.file.StandardCopyOption.ATOMIC_MOVE);
            }
            catch (java.nio.file.AtomicMoveNotSupportedException ignored) {
                Files.move(temporary, configPath, java.nio.file.StandardCopyOption.REPLACE_EXISTING);
            }
        }
        catch (IOException ignored) {
            // El preset sigue activo durante la sesión aunque el disco no sea escribible.
        }
    }
}
