import type { CSSProperties } from "react";
import { Search, Plus, Play, Loader2 } from "lucide-react";
import { useMemo, useState } from "react";
import { defaultArtworkPlacement, type NexaProfile } from "../app/types";
import { ArtworkViewport } from "../components/ArtworkViewport";
import { ProfileCard } from "../components/ProfileCard";

type LibraryPageProps = {
  profiles: NexaProfile[];
  launchingProfileId?: string | null;
  onCreate(): void;
  onOpen(profile: NexaProfile): void;
  onPlay(profile: NexaProfile): void;
};

type ArtworkCss = CSSProperties & {
  "--nexa-bg-position"?: string;
  "--nexa-bg-fit"?: string;
};

export function LibraryPage({ profiles, launchingProfileId, onCreate, onOpen, onPlay }: LibraryPageProps) {
  const [query, setQuery] = useState("");
  const visible = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return profiles;
    return profiles.filter((profile) => `${profile.name} ${profile.minecraftVersion} ${profile.loader}`.toLowerCase().includes(q));
  }, [profiles, query]);
  const recent = profiles[0];
  const recentArtwork = recent?.artwork ?? defaultArtworkPlacement;
  const recentStyle: ArtworkCss | undefined = recent?.backgroundDataUrl ? {
    backgroundImage: `linear-gradient(90deg, rgba(9,13,19,.96), rgba(9,13,19,.45)), url(${recent.backgroundDataUrl})`,
    "--nexa-bg-position": `${recentArtwork.backgroundPositionX}% ${recentArtwork.backgroundPositionY}%`,
    "--nexa-bg-fit": recentArtwork.backgroundFit,
  } : undefined;

  return (
    <section className="page library-page">
      <div className="hero-row">
        <div>
          <span className="eyebrow">TU MINECRAFT. TU ESPACIO.</span>
          <h1>Biblioteca</h1>
          <p>Perfiles aislados, contenido administrable y rendimiento bajo control.</p>
        </div>
        <button className="primary-button" type="button" onClick={onCreate}><Plus size={17} /> NUEVO PERFIL</button>
      </div>

      {recent && (
        <div className="continue-panel glass-panel" style={recentStyle} onClick={() => onOpen(recent)} role="button" tabIndex={0}>
          <div className="continue-icon">
            <ArtworkViewport
              src={recent.iconDataUrl ?? "./brand/nexa-mark.png"}
              fit={recentArtwork.iconFit}
              positionX={recentArtwork.iconPositionX}
              positionY={recentArtwork.iconPositionY}
              zoom={recentArtwork.iconZoom}
              className="continue-icon-viewport"
            />
          </div>
          <div className="continue-copy">
            <span>CONTINUAR JUGANDO</span>
            <h2>{recent.name}</h2>
            <p>{recent.loader} · Minecraft {recent.minecraftVersion}</p>
          </div>
          <button className="play-button" type="button" disabled={launchingProfileId === recent.id} onClick={(event) => { event.stopPropagation(); onPlay(recent); }}>
            {launchingProfileId === recent.id ? <Loader2 className="spin" size={18} /> : <Play size={18} fill="currentColor" />} INICIAR
          </button>
        </div>
      )}

      <div className="library-toolbar">
        <div className="search-field"><Search size={17} /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Buscar perfiles" /></div>
        <span>{visible.length} {visible.length === 1 ? "perfil" : "perfiles"}</span>
      </div>

      {visible.length > 0 ? (
        <div className="profile-grid">
          {visible.map((profile) => <ProfileCard key={profile.id} profile={profile} launching={launchingProfileId === profile.id} onOpen={onOpen} onPlay={onPlay} />)}
        </div>
      ) : (
        <div className="empty-state glass-panel">
          <img className="empty-brand-mark" src="./brand/nexa-mark.png" alt="NEXA" />
          <h2>{profiles.length ? "No encontramos perfiles" : "Tu biblioteca está lista"}</h2>
          <p>{profiles.length ? "Prueba con otra búsqueda." : "Crea tu primer perfil y NEXA mantendrá mundos, mods y configuración totalmente aislados."}</p>
          {!profiles.length && <button className="primary-button" type="button" onClick={onCreate}><Plus size={17} /> CREAR PERFIL</button>}
        </div>
      )}
    </section>
  );
}
