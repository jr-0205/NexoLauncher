package com.nexoclient.ingame;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.util.math.MatrixStack;
import java.util.Locale;

final class NexaLegacyHud {
    private static long windowStart = System.nanoTime();
    private static int frames;
    private static int fps;

    private NexaLegacyHud() { }

    static void render(MinecraftClient client, MatrixStack matrices, NexaLegacyState state) {
        frames++;
        long now = System.nanoTime();
        if (now - windowStart >= 1_000_000_000L) {
            fps = frames;
            frames = 0;
            windowStart = now;
        }
        if (client.player == null || client.textRenderer == null) return;
        int y = 6;
        if (state.fps()) {
            int safeFps = Math.max(1, fps);
            client.textRenderer.drawWithShadow(matrices,
                String.format(Locale.ROOT, "NEXA · %d FPS · ≈ %.1f ms", fps, 1000.0d / safeFps), 6, y, 0xF4F7FC);
            y += 11;
        }
        if (state.coordinates()) {
            client.textRenderer.drawWithShadow(matrices,
                String.format(Locale.ROOT, "XYZ %.1f / %.1f / %.1f", client.player.getX(), client.player.getY(), client.player.getZ()),
                6, y, 0xF4F7FC);
        }
    }
}
