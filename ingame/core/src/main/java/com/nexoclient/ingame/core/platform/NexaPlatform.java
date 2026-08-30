package com.nexoclient.ingame.core.platform;

/**
 * Contrato mínimo entre el core de NEXA y una versión concreta de Minecraft.
 * Ningún tipo de Minecraft/Fabric/Forge debe filtrarse a esta interfaz.
 */
public interface NexaPlatform {
    String minecraftVersion();
    String loader();

    int fps();
    int pingMillis();
    NexaPosition playerPosition();

    int screenWidth();
    int screenHeight();

    void openControlCenter();
    void requestCloseScreen();

    void setRenderDistance(int chunks);
    void setSimulationDistance(int chunks);
    void setEntityDistanceScaling(double scaling);
}
