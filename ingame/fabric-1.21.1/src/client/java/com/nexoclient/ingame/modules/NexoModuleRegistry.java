package com.nexoclient.ingame.modules;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

public final class NexoModuleRegistry {
    private final Map<String, NexoModule> modules = new LinkedHashMap<>();

    public NexoModuleRegistry() {
        register(new NexoModule("fps", "FPS / Frametime", "Muestra FPS y tiempo aproximado por frame", true, true));
        register(new NexoModule("coordinates", "Coordenadas", "Muestra X, Y y Z sin abrir F3", true, true));
        register(new NexoModule("keystrokes", "Keystrokes", "WASD, salto y clics", false, false));
        register(new NexoModule("cps", "CPS", "Contador de clics por segundo", false, false));
        register(new NexoModule("zoom", "Zoom", "Zoom configurable del cliente", false, false));
        register(new NexoModule("toggle_sprint", "Toggle Sprint", "Sprint persistente configurable", false, false));
        register(new NexoModule("hud_editor", "HUD Editor", "Posición y escala de módulos", false, false));
    }

    private void register(NexoModule module) {
        modules.put(module.id(), module);
    }

    public List<NexoModule> all() {
        return List.copyOf(modules.values());
    }

    public boolean enabled(String id) {
        var module = modules.get(id);
        return module != null && module.enabled();
    }
}
