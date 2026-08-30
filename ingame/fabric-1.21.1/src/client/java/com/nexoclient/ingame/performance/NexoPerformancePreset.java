package com.nexoclient.ingame.performance;

import com.nexoclient.ingame.core.performance.NexaParticleMode;
import com.nexoclient.ingame.core.performance.NexaPerformanceProfile;

/**
 * Adaptador 1.21 de los perfiles de rendimiento comunes.
 *
 * No importa ParticlesMode de Minecraft: Mojang cambió esa clase de paquete
 * entre 1.21.1 y 1.21.8. El core conserva la intención y el controller la
 * traduce al enum real que expone GameOptions en la versión ejecutada.
 */
public enum NexoPerformancePreset {
    LOW(NexaPerformanceProfile.LOW),
    MEDIUM_LOW(NexaPerformanceProfile.MEDIUM_LOW),
    MEDIUM(NexaPerformanceProfile.MEDIUM),
    MEDIUM_HIGH(NexaPerformanceProfile.MEDIUM_HIGH),
    HIGH(NexaPerformanceProfile.HIGH);

    private final NexaPerformanceProfile core;

    NexoPerformancePreset(NexaPerformanceProfile core) {
        this.core = core;
    }

    public String displayName() { return core.displayName(); }
    public String description() { return core.description(); }
    public int renderDistance() { return core.renderDistance(); }
    public int simulationDistance() { return core.simulationDistance(); }
    public double entityDistanceScaling() { return core.entityDistanceScaling(); }
    public NexaParticleMode particleMode() { return core.particleMode(); }
    public NexaPerformanceProfile coreProfile() { return core; }
}
