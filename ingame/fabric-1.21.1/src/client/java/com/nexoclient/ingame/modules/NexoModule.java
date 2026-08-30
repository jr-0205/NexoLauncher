package com.nexoclient.ingame.modules;

public final class NexoModule {
    private final String id;
    private final String name;
    private final String description;
    private final boolean ready;
    private boolean enabled;

    public NexoModule(String id, String name, String description, boolean ready, boolean enabled) {
        this.id = id;
        this.name = name;
        this.description = description;
        this.ready = ready;
        this.enabled = ready && enabled;
    }

    public String id() { return id; }
    public String name() { return name; }
    public String description() { return description; }
    public boolean ready() { return ready; }
    public boolean enabled() { return enabled; }

    public void toggle() {
        if (ready) enabled = !enabled;
    }
}
