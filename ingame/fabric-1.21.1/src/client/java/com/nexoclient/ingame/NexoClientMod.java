package com.nexoclient.ingame;

import com.nexoclient.ingame.modules.NexoModuleRegistry;
import com.nexoclient.ingame.performance.NexoPerformanceController;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;

public final class NexoClientMod implements ClientModInitializer {
    public static final NexoModuleRegistry MODULES = new NexoModuleRegistry();
    public static final NexoPerformanceController PERFORMANCE = new NexoPerformanceController();

    private static KeyBinding openMenu;
    private static boolean performanceFaulted;
    private static boolean menuFaulted;
    private static boolean hudFaulted;

    @Override
    public void onInitializeClient() {
        openMenu = KeyBindingHelper.registerKeyBinding(new KeyBinding(
            "key.nexo_ingame.open_menu",
            InputUtil.Type.KEYSYM,
            GLFW.GLFW_KEY_RIGHT_SHIFT,
            "category.nexo_ingame"
        ));

        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            if (!performanceFaulted) {
                try {
                    PERFORMANCE.applyIfPending(client);
                }
                catch (RuntimeException | LinkageError error) {
                    performanceFaulted = true;
                    reportDisabled("rendimiento", error);
                }
            }

            if (!menuFaulted) {
                try {
                    while (openMenu.wasPressed()) {
                        client.setScreen(new NexoMenuScreen(client.currentScreen, MODULES, PERFORMANCE));
                    }
                }
                catch (RuntimeException | LinkageError error) {
                    menuFaulted = true;
                    reportDisabled("Control Center", error);
                }
            }
        });

        HudRenderCallback.EVENT.register((context, tickCounter) -> {
            if (hudFaulted) return;
            try {
                NexoHudOverlay.render(MinecraftClient.getInstance(), context, MODULES);
            }
            catch (RuntimeException | LinkageError error) {
                hudFaulted = true;
                reportDisabled("HUD", error);
            }
        });
    }

    private static void reportDisabled(String component, Throwable error) {
        System.err.println("[NEXA In-Game] " + component + " desactivado por incompatibilidad; Minecraft seguirá ejecutándose.");
        error.printStackTrace(System.err);
    }
}
