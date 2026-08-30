package com.nexoclient.ingame.core.modules;

import java.util.List;

import static com.nexoclient.ingame.core.modules.NexaModuleCategory.*;

public final class NexaModuleCatalog {
    private static final List<NexaModuleSpec> DEFAULTS = List.of(
        spec("fps", "FPS / Frametime", "FPS actuales y tiempo aproximado por frame", HUD, true, true),
        spec("cps", "CPS", "Clics por segundo para botón izquierdo y derecho", PVP, true, false),
        spec("ping", "Ping", "Latencia actual hacia el servidor", HUD, true, false),
        spec("memory", "Memoria", "Uso de memoria de la JVM y presión del heap", HUD, true, false),
        spec("keystrokes", "Keystrokes", "WASD, salto y botones del ratón", PVP, true, false),
        spec("coordinates", "Coordenadas", "Posición X, Y y Z sin abrir F3", HUD, true, true),
        spec("direction", "Dirección", "Orientación cardinal y rumbo", HUD, true, false),
        spec("clock", "Reloj", "Hora local o del mundo en el HUD", HUD, true, false),
        spec("armor_status", "Armor Status", "Durabilidad y estado del equipo", HUD, true, false),
        spec("potion_effects", "Potion Effects", "Efectos activos y duración restante", HUD, true, false),
        spec("zoom", "Zoom", "Zoom configurable sin modificar el FOV permanentemente", UTILITY, false, false),
        spec("freelook", "Freelook", "Mover la cámara sin cambiar la dirección del jugador", UTILITY, false, false),
        spec("snaplook", "Snaplook", "Vista rápida mientras se mantiene una tecla", UTILITY, false, false),
        spec("toggle_sprint", "Toggle Sprint", "Sprint persistente configurable", UTILITY, false, false),
        spec("crosshair", "Crosshair", "Retícula configurable", PVP, true, false),
        spec("hit_color", "Hit Color", "Color de impacto configurable", PVP, false, false),
        spec("attack_indicator", "Attack Indicator", "Indicador de cooldown y ataque", PVP, true, false),
        spec("waypoints", "Waypoints", "Puntos guardados por mundo o servidor", WORLD, true, false),
        spec("minimap", "Minimap", "Mapa compacto con capas configurables", WORLD, true, false),
        spec("chunk_borders", "Chunk Borders", "Límites de chunks sin depender de F3", WORLD, false, false),
        spec("chat", "Chat", "Apariencia y comportamiento del chat", VISUAL, false, false),
        spec("tab", "Tab", "Personalización de la lista de jugadores", VISUAL, false, false),
        spec("scoreboard", "Scoreboard", "Posición, escala y estilo del marcador", VISUAL, true, false),
        spec("nametags", "Nametags", "Presentación configurable de nombres", VISUAL, false, false),
        spec("menu_blur", "Menu Blur", "Desenfoque del fondo en interfaces NEXA", VISUAL, false, false),
        spec("motion_blur", "Motion Blur", "Desenfoque de movimiento opcional", VISUAL, false, false),
        spec("fov", "FOV", "Perfiles y ajustes de campo de visión", VISUAL, false, false),
        spec("pack_organizer", "Pack Organizer", "Organización rápida de resource packs", UTILITY, false, false),
        spec("pack_display", "Pack Display", "Muestra el pack activo en el HUD", HUD, true, false),
        spec("inventory", "Inventory", "Previsualización compacta del inventario", HUD, true, false),
        spec("shulker_preview", "Shulker Preview", "Vista previa del contenido de shulkers", UTILITY, false, false),
        spec("replay", "Replay", "Infraestructura para grabación y reproducción cuando exista adaptador", UTILITY, false, false),
        spec("screenshot", "Screenshot", "Capturas con flujo y metadatos NEXA", UTILITY, false, false),
        spec("hud_editor", "HUD Editor", "Editor visual con ancla, offset, escala y drag & drop", HUD, false, false)
    );

    private NexaModuleCatalog() { }

    public static List<NexaModuleSpec> defaults() {
        return DEFAULTS;
    }

    public static NexaModuleSpec find(String id) {
        if (id == null) return null;
        for (var module : DEFAULTS) {
            if (module.id().equals(id)) return module;
        }
        return null;
    }

    private static NexaModuleSpec spec(
        String id,
        String name,
        String description,
        NexaModuleCategory category,
        boolean hudModule,
        boolean enabledByDefault
    ) {
        return new NexaModuleSpec(id, name, description, category, hudModule, enabledByDefault);
    }
}
