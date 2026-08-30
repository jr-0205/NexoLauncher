package com.nexoclient.ingame;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.fabric.api.client.rendering.v1.HudRenderCallback;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.option.ParticlesMode;
import net.minecraft.client.util.InputUtil;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.text.Text;
import org.lwjgl.glfw.GLFW;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.Locale;
import java.util.Properties;

public final class NexaCompatClient implements ClientModInitializer {
    private static final State STATE=new State(); private KeyBinding menu;
    @Override public void onInitializeClient(){
        menu=KeyBindingHelper.registerKeyBinding(new KeyBinding("key.nexo_ingame.open_menu",InputUtil.Type.KEYSYM,GLFW.GLFW_KEY_RIGHT_SHIFT,"category.nexo_ingame"));
        ClientTickEvents.END_CLIENT_TICK.register(c->{STATE.apply(c);while(menu.wasPressed())c.setScreen(new Menu(c.currentScreen));});
        HudRenderCallback.EVENT.register((m,d)->Hud.render(MinecraftClient.getInstance(),m));
    }
    private enum Preset {LOW("Bajo",7,5,.60,ParticlesMode.MINIMAL),MEDIUM_LOW("Medio-bajo",9,6,.72,ParticlesMode.DECREASED),MEDIUM("Medio",12,8,.85,ParticlesMode.ALL),MEDIUM_HIGH("Medio-alto",16,10,.95,ParticlesMode.ALL),HIGH("Alto",20,12,1.0,ParticlesMode.ALL);final String l;final int r,s;final double e;final ParticlesMode p;Preset(String l,int r,int s,double e,ParticlesMode p){this.l=l;this.r=r;this.s=s;this.e=e;this.p=p;}}
    private static final class State{
        private final Path path=FabricLoader.getInstance().getConfigDir().resolve("nexa-ingame.properties");Preset preset=Preset.MEDIUM;boolean fps=true,coords=true,pending=true;State(){load();}
        void choose(Preset p){preset=p;pending=true;save();}void toggleFps(){fps=!fps;save();}void toggleCoords(){coords=!coords;save();}
        void apply(MinecraftClient c){if(!pending||c.options==null)return;c.options.getViewDistance().setValue(preset.r);c.options.getSimulationDistance().setValue(preset.s);c.options.getEntityDistanceScaling().setValue(preset.e);c.options.getParticles().setValue(preset.p);c.options.write();pending=false;}
        void load(){if(!Files.isRegularFile(path))return;Properties p=new Properties();try(Reader r=Files.newBufferedReader(path,StandardCharsets.UTF_8)){p.load(r);String raw=p.getProperty("performancePreset","MEDIUM");if("MAX_FPS".equalsIgnoreCase(raw))raw="LOW";preset=Preset.valueOf(raw);fps=Boolean.parseBoolean(p.getProperty("fps","true"));coords=Boolean.parseBoolean(p.getProperty("coordinates","true"));}catch(Exception ignored){preset=Preset.MEDIUM;}}
        void save(){Properties p=new Properties();p.setProperty("performancePreset",preset.name());p.setProperty("fps",Boolean.toString(fps));p.setProperty("coordinates",Boolean.toString(coords));try{Files.createDirectories(path.getParent());Path t=path.resolveSibling(path.getFileName()+".tmp");try(Writer w=Files.newBufferedWriter(t,StandardCharsets.UTF_8)){p.store(w,"NEXA In-Game settings");}try{Files.move(t,path,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);}catch(AtomicMoveNotSupportedException ex){Files.move(t,path,StandardCopyOption.REPLACE_EXISTING);}}catch(IOException ignored){}}
    }
    private static final class Menu extends Screen{
        final Screen parent;Menu(Screen p){super(Text.literal("NEXA Client"));parent=p;}
        @Override protected void init(){int bw=116,g=5,total=bw*5+g*4,x=Math.max(8,(width-total)/2),y=72;Preset[] ps=Preset.values();for(int i=0;i<ps.length;i++){final Preset p=ps[i];addDrawableChild(ButtonWidget.builder(Text.literal(p.l+(STATE.preset==p?" · ACTIVO":"")),b->{STATE.choose(p);refresh();}).dimensions(x+i*(bw+g),y,bw,20).build());}addDrawableChild(ButtonWidget.builder(Text.literal("FPS / Frametime · "+(STATE.fps?"ON":"OFF")),b->{STATE.toggleFps();refresh();}).dimensions(width/2-154,112,150,20).build());addDrawableChild(ButtonWidget.builder(Text.literal("Coordenadas · "+(STATE.coords?"ON":"OFF")),b->{STATE.toggleCoords();refresh();}).dimensions(width/2+4,112,150,20).build());addDrawableChild(ButtonWidget.builder(Text.literal("Cerrar"),b->close()).dimensions(width/2-60,150,120,20).build());}
        void refresh(){if(client!=null)client.setScreen(new Menu(parent));}@Override public void render(MatrixStack m,int mx,int my,float d){renderBackground(m);drawCenteredTextWithShadow(m,textRenderer,"NEXA CLIENT",width/2,18,0xF4F7FC);drawCenteredTextWithShadow(m,textRenderer,"Right Shift · Control Center 1.19.x",width/2,34,0xAFC0D8);drawCenteredTextWithShadow(m,textRenderer,"RENDIMIENTO · "+STATE.preset.l,width/2,52,0xF4F7FC);super.render(m,mx,my,d);}@Override public void close(){if(client!=null)client.setScreen(parent);}@Override public boolean shouldPause(){return false;}}
    private static final class Hud{static long start=System.nanoTime();static int frames,fps;static void render(MinecraftClient c,MatrixStack m){frames++;long n=System.nanoTime();if(n-start>=1_000_000_000L){fps=frames;frames=0;start=n;}if(c.player==null)return;int y=6;if(STATE.fps){int f=Math.max(1,fps);c.textRenderer.drawWithShadow(m,String.format(Locale.ROOT,"NEXA · %d FPS · ≈ %.1f ms",fps,1000d/f),6,y,0xF4F7FC);y+=11;}if(STATE.coords)c.textRenderer.drawWithShadow(m,String.format(Locale.ROOT,"XYZ %.1f / %.1f / %.1f",c.player.getX(),c.player.getY(),c.player.getZ()),6,y,0xF4F7FC);}}
}
