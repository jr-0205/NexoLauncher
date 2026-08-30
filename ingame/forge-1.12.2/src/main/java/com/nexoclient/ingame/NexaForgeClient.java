package com.nexoclient.ingame;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiButton;
import net.minecraft.client.gui.GuiScreen;
import net.minecraft.client.settings.KeyBinding;
import net.minecraftforge.client.ClientRegistry;
import net.minecraftforge.client.event.RenderGameOverlayEvent;
import net.minecraftforge.common.MinecraftForge;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.common.event.FMLInitializationEvent;
import net.minecraftforge.fml.common.eventhandler.SubscribeEvent;
import net.minecraftforge.fml.common.gameevent.InputEvent;
import org.lwjgl.input.Keyboard;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Locale;
import java.util.Properties;

@Mod(modid = NexaForgeClient.MODID, name = "NEXA In-Game", version = "0.1.0", clientSideOnly = true, acceptedMinecraftVersions = "[1.12.2]")
public final class NexaForgeClient {
    static final String MODID = "nexo_ingame";
    private final NexaState state = new NexaState();
    private KeyBinding openMenu;

    @Mod.EventHandler public void init(FMLInitializationEvent event){openMenu=new KeyBinding("key.nexo_ingame.open_menu",Keyboard.KEY_RSHIFT,"NEXA In-Game");ClientRegistry.registerKeyBinding(openMenu);MinecraftForge.EVENT_BUS.register(this);state.apply();}
    @SubscribeEvent public void key(InputEvent.KeyInputEvent event){if(openMenu!=null&&openMenu.isPressed()){Minecraft mc=Minecraft.getMinecraft();mc.displayGuiScreen(new NexaMenu(mc.currentScreen,state));}}
    @SubscribeEvent public void hud(RenderGameOverlayEvent.Text event){Minecraft mc=Minecraft.getMinecraft();if(mc.player==null||mc.fontRenderer==null)return;int y=6;if(state.fps){int fps=Math.max(1,Minecraft.getDebugFPS());mc.fontRenderer.drawStringWithShadow(String.format(Locale.ROOT,"NEXA · %d FPS · ≈ %.1f ms",fps,1000.0d/fps),6,y,0xF4F7FC);y+=11;}if(state.coordinates)mc.fontRenderer.drawStringWithShadow(String.format(Locale.ROOT,"XYZ %.1f / %.1f / %.1f",mc.player.posX,mc.player.posY,mc.player.posZ),6,y,0xF4F7FC);}

    static final class NexaState {
        enum Preset {LOW("Bajo",5,2),MEDIUM_LOW("Medio-bajo",7,1),MEDIUM("Medio",9,0),MEDIUM_HIGH("Medio-alto",12,0),HIGH("Alto",16,0);final String label;final int chunks;final int particles;Preset(String l,int c,int p){label=l;chunks=c;particles=p;}}
        Preset preset=Preset.MEDIUM;boolean fps=true;boolean coordinates=true;private final File file=new File(new File(Minecraft.getMinecraft().mcDataDir,"config"),"nexa-ingame.properties");
        NexaState(){load();}void choose(Preset p){preset=p;apply();save();}void toggleFps(){fps=!fps;save();}void toggleCoordinates(){coordinates=!coordinates;save();}
        void apply(){Minecraft mc=Minecraft.getMinecraft();if(mc.gameSettings==null)return;mc.gameSettings.renderDistanceChunks=preset.chunks;mc.gameSettings.particleSetting=preset.particles;mc.gameSettings.saveOptions();}
        private void load(){if(!file.isFile())return;Properties p=new Properties();try(Reader r=new InputStreamReader(new FileInputStream(file),StandardCharsets.UTF_8)){p.load(r);String raw=p.getProperty("performancePreset","MEDIUM");if("MAX_FPS".equalsIgnoreCase(raw))raw="LOW";preset=Preset.valueOf(raw);fps=Boolean.parseBoolean(p.getProperty("fps","true"));coordinates=Boolean.parseBoolean(p.getProperty("coordinates","true"));}catch(IOException|IllegalArgumentException ignored){preset=Preset.MEDIUM;}}
        private void save(){Properties p=new Properties();p.setProperty("performancePreset",preset.name());p.setProperty("fps",Boolean.toString(fps));p.setProperty("coordinates",Boolean.toString(coordinates));File parent=file.getParentFile();if(parent!=null)parent.mkdirs();File tmp=new File(file.getPath()+".tmp");try(Writer w=new OutputStreamWriter(new FileOutputStream(tmp),StandardCharsets.UTF_8)){p.store(w,"NEXA In-Game settings");}catch(IOException ignored){return;}if(file.exists()&&!file.delete())return;if(!tmp.renameTo(file)){try{copy(tmp,file);tmp.delete();}catch(IOException ignored){}}}
        private static void copy(File source,File dest)throws IOException{try(InputStream in=new FileInputStream(source);OutputStream out=new FileOutputStream(dest)){byte[] b=new byte[8192];int n;while((n=in.read(b))>=0)out.write(b,0,n);}}
    }

    static final class NexaMenu extends GuiScreen {
        private final GuiScreen parent;private final NexaState state;NexaMenu(GuiScreen p,NexaState s){parent=p;state=s;}
        @Override public void initGui(){buttonList.clear();int bw=110,gap=4,total=bw*5+gap*4,x=Math.max(5,(width-total)/2),y=68;NexaState.Preset[] values=NexaState.Preset.values();for(int i=0;i<values.length;i++){NexaState.Preset p=values[i];buttonList.add(new GuiButton(10+i,x+i*(bw+gap),y,bw,20,p.label+(state.preset==p?" · ACTIVO":"")));}buttonList.add(new GuiButton(20,width/2-154,108,150,20,"FPS / Frametime · "+(state.fps?"ON":"OFF")));buttonList.add(new GuiButton(21,width/2+4,108,150,20,"Coordenadas · "+(state.coordinates?"ON":"OFF")));buttonList.add(new GuiButton(99,width/2-60,146,120,20,"Cerrar"));}
        @Override protected void actionPerformed(GuiButton button)throws IOException{if(button.id>=10&&button.id<15)state.choose(NexaState.Preset.values()[button.id-10]);else if(button.id==20)state.toggleFps();else if(button.id==21)state.toggleCoordinates();else if(button.id==99){mc.displayGuiScreen(parent);return;}initGui();}
        @Override public void drawScreen(int mouseX,int mouseY,float partialTicks){drawDefaultBackground();drawCenteredString(fontRenderer,"NEXA CLIENT",width/2,16,0xF4F7FC);drawCenteredString(fontRenderer,"Right Shift · Control Center 1.12.x",width/2,31,0xAFC0D8);drawCenteredString(fontRenderer,"RENDIMIENTO · "+state.preset.label,width/2,48,0xF4F7FC);super.drawScreen(mouseX,mouseY,partialTicks);}
        @Override public boolean doesGuiPauseGame(){return false;}
    }
}
