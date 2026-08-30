package com.nexoclient.ingame;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;

public final class NexaLegacyClient implements ClientModInitializer {
    static final NexaLegacyState STATE = new NexaLegacyState();
    private KeyBinding openMenu;

    @Override public void onInitializeClient() {
        openMenu = KeyBindingHelper.registerKeyBinding(new KeyBinding(
            "key.nexo_ingame.open_menu", InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_RIGHT_SHIFT, "category.nexo_ingame"));
        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            STATE.applyIfPending(client);
            while (openMenu.wasPressed()) client.setScreen(new NexaLegacyMenu(client.currentScreen, STATE));
        });
        HudRenderCallback.EVENT.register((matrices, tickDelta) -> NexaLegacyHud.render(MinecraftClient.getInstance(), matrices, STATE));
    }
}
