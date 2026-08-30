package com.nexoclient.ingame;

import com.nexoclient.ingame.modules.NexoModule;
import com.nexoclient.ingame.modules.NexoModuleRegistry;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;

public final class NexoMenuScreen extends Screen {
    private final Screen parent;
    private final NexoModuleRegistry modules;

    public NexoMenuScreen(Screen parent, NexoModuleRegistry modules) {
        super(Text.literal("NEXO Client"));
        this.parent = parent;
        this.modules = modules;
    }

    @Override
    protected void init() {
        int buttonWidth = 190;
        int gap = 8;
        int columns = this.width >= 520 ? 2 : 1;
        int totalWidth = columns == 2 ? buttonWidth * 2 + gap : buttonWidth;
        int startX = (this.width - totalWidth) / 2;
        int startY = 58;

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

    private static Text label(NexoModule module) {
        if (!module.ready()) return Text.literal(module.name() + " · EN DESARROLLO");
        return Text.literal(module.name() + (module.enabled() ? " · ON" : " · OFF"));
    }

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float delta) {
        renderBackground(context, mouseX, mouseY, delta);
        context.drawCenteredTextWithShadow(textRenderer, Text.literal("NEXO CLIENT"), width / 2, 20, 0xF4F7FC);
        context.drawCenteredTextWithShadow(textRenderer,
            Text.literal("Módulos del cliente · Right Shift para abrir"), width / 2, 35, 0xB4C0D2);
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
