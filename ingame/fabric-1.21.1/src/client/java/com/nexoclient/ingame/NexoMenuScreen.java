package com.nexoclient.ingame;

import com.nexoclient.ingame.modules.NexoModule;
import com.nexoclient.ingame.modules.NexoModuleRegistry;
import com.nexoclient.ingame.performance.NexoPerformanceController;
import com.nexoclient.ingame.performance.NexoPerformancePreset;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;

import java.util.ArrayList;
import java.util.List;

public final class NexoMenuScreen extends Screen {
    private static final int NEXA_BACKGROUND = 0xCC050910;
    private final Screen parent;
    private final NexoModuleRegistry modules;
    private final NexoPerformanceController performance;
    private final List<ButtonWidget> presetButtons = new ArrayList<>();
    private int modulesStartY;

    public NexoMenuScreen(Screen parent, NexoModuleRegistry modules, NexoPerformanceController performance) {
        super(Text.literal("NEXA Client"));
        this.parent = parent;
        this.modules = modules;
        this.performance = performance;
    }

    @Override
    protected void init() {
        presetButtons.clear();
        modulesStartY = addPerformancePresetButtons();

        int buttonWidth = 190;
        int gap = 8;
        int columns = this.width >= 520 ? 2 : 1;
        int totalWidth = columns == 2 ? buttonWidth * 2 + gap : buttonWidth;
        int startX = (this.width - totalWidth) / 2;
        int startY = modulesStartY;

        int index = 0;
        for (NexoModule module : modules.all()) {
            int column = columns == 2 ? index % 2 : 0;
            int row = columns == 2 ? index / 2 : index;
            int x = startX + column * (buttonWidth + gap);
            int y = startY + row * 28;

            ButtonWidget button = ButtonWidget.builder(label(module), clicked -> {
                module.toggle();
                clicked.setMessage(label(module));
            }).dimensions(x, y, buttonWidth, 20).build();
            button.active = module.ready();
            addDrawableChild(button);
            index++;
        }

        int rows = (modules.all().size() + columns - 1) / columns;
        addDrawableChild(ButtonWidget.builder(Text.literal("Cerrar"), button -> close())
            .dimensions((this.width - 120) / 2, startY + rows * 28 + 12, 120, 20)
            .build());
    }

    private int addPerformancePresetButtons() {
        NexoPerformancePreset[] presets = NexoPerformancePreset.values();
        int gap = 6;
        int columns = this.width >= 720 ? 5 : this.width >= 520 ? 3 : 2;
        int buttonWidth = columns == 5 ? 118 : columns == 3 ? 140 : 150;
        int totalWidth = columns * buttonWidth + (columns - 1) * gap;
        int startX = (this.width - totalWidth) / 2;
        int startY = 78;

        for (int index = 0; index < presets.length; index++) {
            NexoPerformancePreset preset = presets[index];
            int column = index % columns;
            int row = index / columns;
            int x = startX + column * (buttonWidth + gap);
            int y = startY + row * 26;
            ButtonWidget button = ButtonWidget.builder(presetLabel(preset), clicked -> {
                if (client != null) performance.applyNow(client, preset);
                else performance.select(preset);
                refreshPresetLabels();
            }).dimensions(x, y, buttonWidth, 20).build();
            presetButtons.add(button);
            addDrawableChild(button);
        }

        int rows = (presets.length + columns - 1) / columns;
        return startY + rows * 26 + 30;
    }

    private void refreshPresetLabels() {
        NexoPerformancePreset[] presets = NexoPerformancePreset.values();
        for (int i = 0; i < presetButtons.size() && i < presets.length; i++) {
            presetButtons.get(i).setMessage(presetLabel(presets[i]));
        }
    }

    private Text presetLabel(NexoPerformancePreset preset) {
        return Text.literal(preset.displayName() + (performance.selected() == preset ? " · ACTIVO" : ""));
    }

    private static Text label(NexoModule module) {
        if (!module.ready()) return Text.literal(module.name() + " · EN DESARROLLO");
        return Text.literal(module.name() + (module.enabled() ? " · ON" : " · OFF"));
    }

    @Override
    public void renderBackground(DrawContext context, int mouseX, int mouseY, float delta) {
        // Never delegate to Screen#renderBackground here. Minecraft 1.21.8 applies
        // its blur from that method and throws when another screen/mod already blurred
        // the same frame. Keeping this override makes every background-render path
        // for NEXA safe, including calls triggered by Screen/Fabric internals.
        context.fill(0, 0, width, height, NEXA_BACKGROUND);
    }

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float delta) {
        renderBackground(context, mouseX, mouseY, delta);
        context.drawCenteredTextWithShadow(textRenderer, Text.literal("NEXA CLIENT"), width / 2, 14, 0xF4F7FC);
        context.drawCenteredTextWithShadow(textRenderer,
            Text.literal("Right Shift · Rendimiento y módulos"), width / 2, 29, 0xB4C0D2);
        context.drawCenteredTextWithShadow(textRenderer,
            Text.literal("RENDIMIENTO · " + performance.selected().displayName()), width / 2, 48, 0xF4F7FC);
        context.drawCenteredTextWithShadow(textRenderer,
            Text.literal(performance.selected().description()), width / 2, 60, 0xB4C0D2);
        context.drawCenteredTextWithShadow(textRenderer,
            Text.literal("MÓDULOS"), width / 2, Math.max(100, modulesStartY - 17), 0xF4F7FC);
        super.render(context, mouseX, mouseY, delta);
    }

    @Override
    public void close() {
        if (client != null) client.setScreen(parent);
    }

    @Override
    public boolean shouldPause() {
        return false;
    }
}
