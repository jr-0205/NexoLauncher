package com.nexoclient.ingame;

import com.nexoclient.ingame.modules.NexoModuleRegistry;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;

import java.util.Locale;

final class NexoHudOverlay {
    private static final int TEXT_COLOR = 0xF4F7FC;

    private NexoHudOverlay() { }

    static void render(MinecraftClient client, DrawContext context, NexoModuleRegistry modules) {
        if (client.player == null || client.textRenderer == null) return;

        int y = 6;
        if (modules.enabled("fps")) {
            int fps = Math.max(1, client.getCurrentFps());
            double frameMs = 1000.0d / fps;
            String text = String.format(Locale.ROOT, "NEXO · %d FPS · ≈ %.1f ms", fps, frameMs);
            context.drawTextWithShadow(client.textRenderer, text, 6, y, TEXT_COLOR);
            y += 11;
        }

        if (modules.enabled("coordinates")) {
            String coords = String.format(Locale.ROOT, "XYZ %.1f / %.1f / %.1f",
                client.player.getX(), client.player.getY(), client.player.getZ());
            context.drawTextWithShadow(client.textRenderer, coords, 6, y, TEXT_COLOR);
        }
    }
}
