import type { CSSProperties } from "react";
import { Loader2, Play, Settings2 } from "lucide-react";
import { motion } from "motion/react";
import { defaultArtworkPlacement, type NexaProfile } from "../app/types";
import { ArtworkViewport } from "./ArtworkViewport";

type ProfileCardProps = {
  profile: NexaProfile;
  launching?: boolean;
  onOpen(profile: NexaProfile): void;
  onPlay(profile: NexaProfile): void;
};

export function ProfileCard({ profile, launching = false, onOpen, onPlay }: ProfileCardProps) {
  const meta = `${profile.loader}${profile.loaderVersion ? ` ${profile.loaderVersion}` : ""} · Minecraft ${profile.minecraftVersion}`;
  const artwork = profile.artwork ?? defaultArtworkPlacement;
  const style: CSSProperties | undefined = profile.backgroundDataUrl
    ? {
        backgroundImage: `linear-gradient(180deg, rgba(7,10,16,.03), rgba(7,10,16,.95)), url(${profile.backgroundDataUrl})`,
        backgroundPosition: `center, ${artwork.backgroundPositionX}% ${artwork.backgroundPositionY}%`,
        backgroundSize: `auto, ${artwork.backgroundFit}`,
        backgroundRepeat: "no-repeat, no-repeat",
      }
    : undefined;

  return (
    <motion.article
      className="profile-card"
      style={style}
      whileHover={{ y: -3 }}
      transition={{ duration: 0.18 }}
      onClick={() => onOpen(profile)}
      role="button"
      tabIndex={0}
      onKeyDown={(event) => (event.key === "Enter" || event.key === " ") && onOpen(profile)}
    >
      <div className="profile-card-top">
        <div className="profile-icon">
          <ArtworkViewport
            src={profile.iconDataUrl ?? "./brand/nexa-mark.png"}
            fit={artwork.iconFit}
            positionX={artwork.iconPositionX}
            positionY={artwork.iconPositionY}
            zoom={artwork.iconZoom}
            className="profile-icon-viewport"
          />
        </div>
        <button className="icon-button" type="button" aria-label={`Abrir ${profile.name}`} onClick={(event) => { event.stopPropagation(); onOpen(profile); }}><Settings2 size={17} /></button>
      </div>
      <div className="profile-card-copy">
        <h3>{profile.name}</h3>
        <p>{meta}</p>
        <span className="ready"><span className="status-dot" /> {launching ? "INICIANDO" : "LISTO"}</span>
      </div>
      <button className="play-fab" type="button" disabled={launching} onClick={(event) => { event.stopPropagation(); onPlay(profile); }} aria-label={`Iniciar ${profile.name}`}>
        {launching ? <Loader2 className="spin" size={17} /> : <Play size={17} fill="currentColor" />}
      </button>
    </motion.article>
  );
}
