import { Loader2, Play, Settings2 } from "lucide-react";
import { motion } from "motion/react";
import type { NexaProfile } from "../app/types";

type ProfileCardProps = {
  profile: NexaProfile;
  launching?: boolean;
  onOpen(profile: NexaProfile): void;
  onPlay(profile: NexaProfile): void;
};

export function ProfileCard({ profile, launching = false, onOpen, onPlay }: ProfileCardProps) {
  const meta = `${profile.loader}${profile.loaderVersion ? ` ${profile.loaderVersion}` : ""} · Minecraft ${profile.minecraftVersion}`;
  const style = profile.backgroundDataUrl
    ? { backgroundImage: `linear-gradient(180deg, rgba(7,10,16,.03), rgba(7,10,16,.95)), url(${profile.backgroundDataUrl})` }
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
        <div className="profile-icon"><img src={profile.iconDataUrl ?? "./brand/nexa-mark.png"} alt="" /></div>
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
