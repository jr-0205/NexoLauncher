import { Play, MoreHorizontal } from "lucide-react";
import { motion } from "motion/react";
import type { NexaProfile } from "../app/types";

type ProfileCardProps = { profile: NexaProfile; onPlay(profile: NexaProfile): void };

export function ProfileCard({ profile, onPlay }: ProfileCardProps) {
  const meta = `${profile.loader}${profile.loaderVersion ? ` ${profile.loaderVersion}` : ""} · Minecraft ${profile.minecraftVersion}`;
  const style = profile.backgroundDataUrl
    ? { backgroundImage: `linear-gradient(180deg, rgba(7,10,16,.03), rgba(7,10,16,.95)), url(${profile.backgroundDataUrl})` }
    : undefined;

  return (
    <motion.article className="profile-card" style={style} whileHover={{ y: -3 }} transition={{ duration: 0.18 }}>
      <div className="profile-card-top">
        <div className="profile-icon">
          <img src={profile.iconDataUrl ?? "./brand/nexa-mark.png"} alt="" />
        </div>
        <button className="icon-button" aria-label={`Más opciones de ${profile.name}`}><MoreHorizontal size={18} /></button>
      </div>
      <div className="profile-card-copy">
        <h3>{profile.name}</h3>
        <p>{meta}</p>
        <span className="ready"><span className="status-dot" /> LISTO</span>
      </div>
      <button className="play-fab" type="button" onClick={() => onPlay(profile)} aria-label={`Iniciar ${profile.name}`}>
        <Play size={17} fill="currentColor" />
      </button>
    </motion.article>
  );
}
