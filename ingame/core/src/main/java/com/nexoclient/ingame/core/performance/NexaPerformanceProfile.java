package com.nexoclient.ingame.core.performance;

public enum NexaPerformanceProfile {
    LOW("Bajo", "Máximo rendimiento; reduce distancia y ambiente", 8, 5, 0.60d),
    MEDIUM_LOW("Medio-bajo", "Rendimiento alto con algo más de distancia y ambiente", 10, 6, 0.72d),
    MEDIUM("Medio", "Equilibrado; conserva feedback visual importante de combate", 12, 8, 0.85d),
    MEDIUM_HIGH("Medio-alto", "Más distancia y calidad manteniendo optimizaciones", 16, 10, 0.95d),
    HIGH("Alto", "Prioriza calidad visual sin desactivar NEXA Boost", 20, 12, 1.00d);

    private final String displayName;
    private final String description;
    private final int renderDistance;
    private final int simulationDistance;
    private final double entityDistanceScaling;

    NexaPerformanceProfile(
        String displayName,
        String description,
        int renderDistance,
        int simulationDistance,
        double entityDistanceScaling
    ) {
        this.displayName = displayName;
        this.description = description;
        this.renderDistance = renderDistance;
        this.simulationDistance = simulationDistance;
        this.entityDistanceScaling = entityDistanceScaling;
    }

    public String displayName() { return displayName; }
    public String description() { return description; }
    public int renderDistance() { return renderDistance; }
    public int simulationDistance() { return simulationDistance; }
    public double entityDistanceScaling() { return entityDistanceScaling; }
}
