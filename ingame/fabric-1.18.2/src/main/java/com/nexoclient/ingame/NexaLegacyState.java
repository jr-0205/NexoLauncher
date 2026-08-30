package com.nexoclient.ingame;

import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.ParticlesMode;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.Properties;

final class NexaLegacyState {
    enum Preset {
        LOW("Bajo", 6, 4, 0.55f, ParticlesMode.MINIMAL),
        MEDIUM_LOW("Medio-bajo", 8, 5, 0.68f, ParticlesMode.DECREASED),
        MEDIUM("Medio", 10, 6, 0.82f, ParticlesMode.ALL),
        MEDIUM_HIGH("Medio-alto", 14, 8, 0.92f, ParticlesMode.ALL),
        HIGH("Alto", 18, 10, 1.00f, ParticlesMode.ALL);
        final String label; final int render; final int simulation; final float entities; final ParticlesMode particles;
        Preset(String label, int render, int simulation, float entities, ParticlesMode particles) {
            this.label=label; this.render=render; this.simulation=simulation; this.entities=entities; this.particles=particles;
        }
    }
    private final Path config = FabricLoader.getInstance().getConfigDir().resolve("nexa-ingame.properties");
    private Preset preset=Preset.MEDIUM; private boolean fps=true; private boolean coordinates=true; private boolean pending=true;
    NexaLegacyState(){ load(); }
    Preset preset(){return preset;} boolean fps(){return fps;} boolean coordinates(){return coordinates;}
    void setPreset(Preset p){preset=p;pending=true;save();} void toggleFps(){fps=!fps;save();} void toggleCoordinates(){coordinates=!coordinates;save();}
    void applyIfPending(MinecraftClient client){
        if(!pending||client==null||client.options==null)return;
        client.options.viewDistance=preset.render;
        client.options.simulationDistance=preset.simulation;
        client.options.entityDistanceScaling=preset.entities;
        client.options.particles=preset.particles;
        client.options.write();
        pending=false;
    }
    private void load(){
        if(!Files.isRegularFile(config))return; Properties p=new Properties();
        try(Reader r=Files.newBufferedReader(config,StandardCharsets.UTF_8)){
            p.load(r); String raw=p.getProperty("performancePreset","MEDIUM"); if("MAX_FPS".equalsIgnoreCase(raw))raw="LOW";
            preset=Preset.valueOf(raw); fps=Boolean.parseBoolean(p.getProperty("fps","true")); coordinates=Boolean.parseBoolean(p.getProperty("coordinates","true"));
        }catch(IOException|IllegalArgumentException ignored){preset=Preset.MEDIUM;}
    }
    private void save(){
        Properties p=new Properties(); p.setProperty("performancePreset",preset.name());p.setProperty("fps",Boolean.toString(fps));p.setProperty("coordinates",Boolean.toString(coordinates));
        try{Files.createDirectories(config.getParent());Path t=config.resolveSibling(config.getFileName()+".tmp");try(Writer w=Files.newBufferedWriter(t,StandardCharsets.UTF_8)){p.store(w,"NEXA In-Game settings");}
            try{Files.move(t,config,StandardCopyOption.REPLACE_EXISTING,StandardCopyOption.ATOMIC_MOVE);}catch(AtomicMoveNotSupportedException ignored){Files.move(t,config,StandardCopyOption.REPLACE_EXISTING);}}
        catch(IOException ignored){}
    }
}
