package com.nexoclient.ingame.modules;

import com.nexoclient.ingame.core.modules.NexaModuleCatalog;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/**
 * Adaptador 1.21 del catálogo común de módulos NEXA.
 *
 * El core define identidad, categoría, copy y defaults. Este adaptador decide
 * qué módulos ya tienen implementación real para esta familia de Minecraft.
 */
public final class NexoModuleRegistry {
    private static final Set<String> READY_ON_1_21 = Set.of(
        "fps",
        "coordinates"
    );

    private final Map<String, NexoModule> modules = new LinkedHashMap<>();

    public NexoModuleRegistry() {
        for (var spec : NexaModuleCatalog.defaults()) {
            var ready = READY_ON_1_21.contains(spec.id());
            register(new NexoModule(
                spec.id(),
                spec.name(),
                spec.description(),
                ready,
                ready && spec.enabledByDefault()
            ));
        }
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
