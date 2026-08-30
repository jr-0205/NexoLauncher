package com.nexoclient.ingame.core.modules;

import java.util.Objects;

public record NexaModuleSpec(
    String id,
    String name,
    String description,
    NexaModuleCategory category,
    boolean hudModule,
    boolean enabledByDefault
) {
    public NexaModuleSpec {
        id = require(id, "id");
        name = require(name, "name");
        description = require(description, "description");
        category = Objects.requireNonNull(category, "category");
    }

    private static String require(String value, String field) {
        if (value == null || value.isBlank()) throw new IllegalArgumentException(field + " no puede estar vacío");
        return value.trim();
    }
}
