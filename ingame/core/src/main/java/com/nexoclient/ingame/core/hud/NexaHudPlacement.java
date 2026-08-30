package com.nexoclient.ingame.core.hud;

public record NexaHudPlacement(
    NexaHudAnchor anchor,
    double offsetX,
    double offsetY,
    double scale
) {
    public NexaHudPlacement {
        if (anchor == null) anchor = NexaHudAnchor.TOP_LEFT;
        scale = Math.max(0.25d, Math.min(scale <= 0 ? 1.0d : scale, 4.0d));
    }

    public static NexaHudPlacement defaults() {
        return new NexaHudPlacement(NexaHudAnchor.TOP_LEFT, 0, 0, 1.0d);
    }
}
