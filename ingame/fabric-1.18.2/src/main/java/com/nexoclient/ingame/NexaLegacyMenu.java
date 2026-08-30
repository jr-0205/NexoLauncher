package com.nexoclient.ingame;

import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.text.LiteralText;

final class NexaLegacyMenu extends Screen {
    private final Screen parent; private final NexaLegacyState state;
    NexaLegacyMenu(Screen parent,NexaLegacyState state){super(new LiteralText("NEXA Client"));this.parent=parent;this.state=state;}
    @Override protected void init(){
        int bw=116,gap=5,total=bw*5+gap*4,x=Math.max(8,(width-total)/2),y=72;
        NexaLegacyState.Preset[] values=NexaLegacyState.Preset.values();
        for(int i=0;i<values.length;i++){final NexaLegacyState.Preset p=values[i];addDrawableChild(new ButtonWidget(x+i*(bw+gap),y,bw,20,new LiteralText(p.label+(state.preset()==p?" · ACTIVO":"")),b->{state.setPreset(p);refresh();}));}
        int my=112;
        addDrawableChild(new ButtonWidget(width/2-154,my,150,20,new LiteralText("FPS / Frametime · "+(state.fps()?"ON":"OFF")),b->{state.toggleFps();refresh();}));
        addDrawableChild(new ButtonWidget(width/2+4,my,150,20,new LiteralText("Coordenadas · "+(state.coordinates()?"ON":"OFF")),b->{state.toggleCoordinates();refresh();}));
        addDrawableChild(new ButtonWidget(width/2-60,my+38,120,20,new LiteralText("Cerrar"),b->close()));
    }
    private void refresh(){if(client!=null)client.setScreen(new NexaLegacyMenu(parent,state));}
    @Override public void render(MatrixStack m,int mouseX,int mouseY,float delta){renderBackground(m);drawCenteredText(m,textRenderer,new LiteralText("NEXA CLIENT"),width/2,18,0xF4F7FC);drawCenteredText(m,textRenderer,new LiteralText("Right Shift · Control Center 1.18.x"),width/2,34,0xAFC0D8);drawCenteredText(m,textRenderer,new LiteralText("RENDIMIENTO · "+state.preset().label),width/2,52,0xF4F7FC);super.render(m,mouseX,mouseY,delta);}
    @Override public void close(){if(client!=null)client.setScreen(parent);}
    @Override public boolean shouldPause(){return false;}
}
