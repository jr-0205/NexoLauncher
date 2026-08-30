package com.nexoclient.ingame.performance;

import net.minecraft.client.option.ParticlesMode;

/**
 * Escala de calidad/rendimiento administrada por NEXA In-Game.
 * LOW prioriza FPS y HIGH prioriza fidelidad visual. Los presets sólo exponen
 * ajustes seguros que pueden cambiarse en caliente; los mods puramente internos
 * (Lithium, FerriteCore, ModernFix, etc.) permanecen activos sin perfiles falsos.
 */
public enum NexoPerformancePreset {
    LOW("Bajo", "Máximo rendimiento; reduce distancia y ambiente", 8, 5, 0.60d, ParticlesMode.MINIMAL),
    MEDIUM_LOW("Medio-bajo", "Rendimiento alto con algo más de distancia y ambiente", 10, 6, 0.72d, ParticlesMode.DECREASED),
    MEDIUM("Medio", "Equilibrado; conserva feedback visual importante de combate", 12, 8, 0.85d, ParticlesMode.ALL),
    MEDIUM_HIGH("Medio-alto", "Más distancia y calidad manteniendo optimizaciones", 16, 10, 0.95d, ParticlesMode.ALL),
    HIGH("Alto", "Prioriza calidad visual sin desactivar NEXA Boost", 20, 12, 1.00d, ParticlesMode.ALL);

    private final String displayName;
    private final String description;
    private final int renderDistance;
    private final int simulationDistance;
    private final double entityDistanceScaling;
    private final ParticlesMode particles;

    NexoPerformancePreset(String displayName, String description, int renderDistance, int simulationDistance,
                          double entityDistanceScaling, ParticlesMode particles) {
        this.displayName = displayName;
        this.description = description;
        this.renderDistance = renderDistance;
        this.simulationDistance = simulationDistance;
        this.entityDistanceScaling = entityDistanceScaling;
        this.particles = particles;
    }

    public String displayName() { return displayName; }
    public String description() { return description; }
    public int renderDistance() { return renderDistance; }
    public int simulationDistance() { return simulationDistance; }
    public double entityDistanceScaling() { return entityDistanceScaling; }
    public ParticlesMode particles() { return particles; }
}
