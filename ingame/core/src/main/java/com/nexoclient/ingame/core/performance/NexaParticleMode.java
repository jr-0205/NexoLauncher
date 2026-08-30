package com.nexoclient.ingame.core.performance;

/**
 * Semántica de partículas estable de NEXA.
 *
 * No depende del enum de Minecraft porque Mojang ha movido ParticlesMode entre
 * paquetes dentro de la familia 1.21. Los adaptadores traducen este valor al
 * tipo concreto disponible en cada versión.
 */
public enum NexaParticleMode {
    ALL,
    DECREASED,
    MINIMAL
}
