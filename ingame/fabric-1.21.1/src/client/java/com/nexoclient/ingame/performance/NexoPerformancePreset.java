package com.nexoclient.ingame.performance;

import com.nexoclient.ingame.core.performance.NexaPerformanceProfile;
import net.minecraft.client.option.ParticlesMode;

/**
 * Adaptador 1.21 de los perfiles de rendimiento comunes.
 * Los valores estables viven en core; aquí sólo traducimos el modo de partículas
 * a la API concreta de Minecraft 1.21.
 */
public enum NexoPerformancePreset {
    LOW(NexaPerformanceProfile.LOW, ParticlesMode.MINIMAL),
    MEDIUM_LOW(NexaPerformanceProfile.MEDIUM_LOW, ParticlesMode.DECREASED),
    MEDIUM(NexaPerformanceProfile.MEDIUM, ParticlesMode.ALL),
    MEDIUM_HIGH(NexaPerformanceProfile.MEDIUM_HIGH, ParticlesMode.ALL),
    HIGH(NexaPerformanceProfile.HIGH, ParticlesMode.ALL);

    private final NexaPerformanceProfile core;
    private final ParticlesMode particles;

    NexoPerformancePreset(NexaPerformanceProfile core, ParticlesMode particles) {
        this.core = core;
        this.particles = particles;
    }

    public String displayName() { return core.displayName(); }
    public String description() { return core.description(); }
    public int renderDistance() { return core.renderDistance(); }
    public int simulationDistance() { return core.simulationDistance(); }
    public double entityDistanceScaling() { return core.entityDistanceScaling(); }
    public ParticlesMode particles() { return particles; }
    public NexaPerformanceProfile coreProfile() { return core; }
}
