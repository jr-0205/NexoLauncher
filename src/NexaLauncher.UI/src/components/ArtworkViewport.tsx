import type { CSSProperties, ReactNode } from "react";

export type ArtworkViewportProps = {
  src: string;
  fit: "cover" | "contain";
  positionX: number;
  positionY: number;
  zoom?: number;
  className?: string;
  alt?: string;
  children?: ReactNode;
};

export function ArtworkViewport({
  src,
  fit,
  positionX,
  positionY,
  zoom = 100,
  className = "",
  alt = "",
  children,
}: ArtworkViewportProps) {
  const safeZoom = Math.max(50, Math.min(300, zoom || 100));
  const imageStyle: CSSProperties = {
    objectFit: fit,
    objectPosition: `${positionX}% ${positionY}%`,
    transform: `scale(${safeZoom / 100})`,
    transformOrigin: `${positionX}% ${positionY}%`,
  };

  return (
    <div className={`artwork-viewport ${className}`.trim()}>
      <img src={src} alt={alt} style={imageStyle} draggable={false} />
      {children}
    </div>
  );
}
