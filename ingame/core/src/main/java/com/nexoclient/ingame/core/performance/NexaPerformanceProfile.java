package com.nexoclient.ingame.core.performance;

public enum NexaPerformanceProfile {
    LOW("Bajo", "Máximo rendimiento; reduce distancia y ambiente", 8, 5, 0.60d, NexaParticleMode.MINIMAL),
    MEDIUM_LOW("Medio-bajo", "Rendimiento alto con algo más de distancia y ambiente", 10, 6, 0.72d, NexaParticleMode.DECREASED),
    MEDIUM("Medio", "Equilibrado; conserva feedback visual importante de combate", 12, 8, 0.85d, NexaParticleMode.ALL),
    MEDIUM_HIGH("Medio-alto", "Más distancia y calidad manteniendo optimizaciones", 16, 10, 0.95d, NexaParticleMode.ALL),
    HIGH("Alto", "Prioriza calidad visual sin desactivar NEXA Boost", 20, 12, 1.00d, NexaParticleMode.ALL);

    private final String displayName;
    private final String description;
    private final int renderDistance;
    private final int simulationDistance;
    private final double entityDistanceScaling;
    private final NexaParticleMode particleMode;

    NexaPerformanceProfile(
        String displayName,
        String description,
        int renderDistance,
        int simulationDistance,
        double entityDistanceScaling,
        NexaParticleMode particleMode
    ) {
        this.displayName = displayName;
        this.description = description;
        this.renderDistance = renderDistance;
        this.simulationDistance = simulationDistance;
        this.entityDistanceScaling = entityDistanceScaling;
        this.particleMode = particleMode;
    }

    public String displayName() { return displayName; }
    public String description() { return description; }
    public int renderDistance() { return renderDistance; }
    public int simulationDistance() { return simulationDistance; }
    public double entityDistanceScaling() { return entityDistanceScaling; }
    public NexaParticleMode particleMode() { return particleMode; }
}
