package com.nexoclient.ingame;

import com.mojang.blaze3d.systems.RenderSystem;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.text.LiteralText;
import net.minecraft.text.Text;

final class NexaLegacyMenu extends Screen {
    private final Screen parent;
    private final NexaLegacyState state;

    NexaLegacyMenu(Screen parent, NexaLegacyState state) {
        super(new LiteralText("NEXA Client"));
        this.parent = parent;
        this.state = state;
    }

    @Override
    protected void init() {
        int width = 116;
        int gap = 5;
        int total = width * 5 + gap * 4;
        int x = Math.max(8, (this.width - total) / 2);
        int y = 72;
        NexaLegacyState.Preset[] presets = NexaLegacyState.Preset.values();
        for (int i = 0; i < presets.length; i++) {
            final NexaLegacyState.Preset preset = presets[i];
            addButton(new ButtonWidget(x + i * (width + gap), y, width, 20,
                new LiteralText(preset.label + (state.preset() == preset ? " · ACTIVO" : "")), button -> {
                    state.setPreset(preset);
                    refresh();
                }));
        }

        int moduleY = 112;
        addButton(new ButtonWidget(this.width / 2 - 154, moduleY, 150, 20,
            new LiteralText("FPS / Frametime · " + (state.fps() ? "ON" : "OFF")), button -> { state.toggleFps(); refresh(); }));
        addButton(new ButtonWidget(this.width / 2 + 4, moduleY, 150, 20,
            new LiteralText("Coordenadas · " + (state.coordinates() ? "ON" : "OFF")), button -> { state.toggleCoordinates(); refresh(); }));
        addButton(new ButtonWidget(this.width / 2 - 60, moduleY + 38, 120, 20,
            new LiteralText("Cerrar"), button -> close()));
    }

    private void refresh() { if (client != null) client.openScreen(new NexaLegacyMenu(parent, state)); }

    @Override
    public void render(MatrixStack matrices, int mouseX, int mouseY, float delta) {
        renderBackground(matrices);
        drawCenteredText(matrices, textRenderer, new LiteralText("NEXA CLIENT"), width / 2, 18, 0xF4F7FC);
        drawCenteredText(matrices, textRenderer, new LiteralText("Right Shift · Control Center 1.16.x"), width / 2, 34, 0xAFC0D8);
        drawCenteredText(matrices, textRenderer, new LiteralText("RENDIMIENTO · " + state.preset().label), width / 2, 52, 0xF4F7FC);
        super.render(matrices, mouseX, mouseY, delta);
    }

    @Override public void onClose() { if (client != null) client.openScreen(parent); }
    private void close() { onClose(); }
    @Override public boolean isPauseScreen() { return false; }
}
