import type { CSSProperties } from "react";
import { useEffect, useRef, useState } from "react";
import { Boxes, Check, FolderOpen, Gauge, ImagePlus, Loader2, Play, RotateCcw, Save, Sparkles, Trash2, X } from "lucide-react";
import { applyBoost, deleteProfile, getBoostStatus, openProfileFolder, removeBoost, updateArtworkPlacement, updateProfile } from "../app/nexa-bridge";
import { defaultArtworkPlacement, type BoostApplyResult, type BoostStatus, type NexaProfile, type ProfileArtworkPlacement } from "../app/types";
import { NexaDialog } from "../components/NexaDialog";

type Props = {
  profile: NexaProfile;
  launching: boolean;
  onLaunch(profile: NexaProfile): Promise<void>;
  onContent(profile: NexaProfile): void;
  onUpdated(profile: NexaProfile): void;
  onDeleted(): void;
  onBack(): void;
  onNotice(message: string, kind?: "success" | "error"): void;
};

async function imageToDataUrl(file: File) {
  if (file.size > 8 * 1024 * 1024) throw new Error("La imagen no puede superar 8 MB.");
  return await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error("No se pudo leer la imagen."));
    reader.readAsDataURL(file);
  });
}

export function ProfileDetailPage({ profile, launching, onLaunch, onContent, onUpdated, onDeleted, onBack, onNotice }: Props) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(profile.name);
  const [description, setDescription] = useState(profile.description ?? "");
  const [iconDataUrl, setIconDataUrl] = useState<string | null>(null);
  const [backgroundDataUrl, setBackgroundDataUrl] = useState<string | null>(null);
  const [removeIcon, setRemoveIcon] = useState(false);
  const [removeBackground, setRemoveBackground] = useState(false);
  const [artwork, setArtwork] = useState<ProfileArtworkPlacement>(profile.artwork ?? defaultArtworkPlacement);
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [boostOpen, setBoostOpen] = useState(false);
  const [boostStatus, setBoostStatus] = useState<BoostStatus | null>(null);
  const [boostBusy, setBoostBusy] = useState(false);
  const [boostSummary, setBoostSummary] = useState<BoostApplyResult | null>(null);
  const [confirmRemoveBoost, setConfirmRemoveBoost] = useState(false);
  const iconInput = useRef<HTMLInputElement>(null);
  const backgroundInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    setArtwork(profile.artwork ?? defaultArtworkPlacement);
    getBoostStatus(profile.id)
      .then(setBoostStatus)
      .catch((error: Error) => onNotice(error.message, "error"));
  }, [profile.id]);

  const shownIcon = removeIcon ? "./brand/nexa-mark.png" : iconDataUrl ?? profile.iconDataUrl ?? "./brand/nexa-mark.png";
  const shownBackground = removeBackground ? null : backgroundDataUrl ?? profile.backgroundDataUrl ?? null;
  const heroStyle: CSSProperties | undefined = shownBackground ? {
    backgroundImage: `linear-gradient(90deg,rgba(6,10,17,.97) 0%,rgba(6,10,17,.72) 48%,rgba(6,10,17,.30) 100%),url(${shownBackground})`,
    backgroundPosition: `center, ${artwork.backgroundPositionX}% ${artwork.backgroundPositionY}%`,
    backgroundSize: `auto, ${artwork.backgroundFit}`,
    backgroundRepeat: "no-repeat, no-repeat",
  } : undefined;
  const iconStyle: CSSProperties = { objectFit: artwork.iconFit, objectPosition: `${artwork.iconPositionX}% ${artwork.iconPositionY}%` };

  async function choose(kind: "icon" | "background", file?: File) {
    if (!file) return;
    try {
      const data = await imageToDataUrl(file);
      if (kind === "icon") { setIconDataUrl(data); setRemoveIcon(false); }
      else { setBackgroundDataUrl(data); setRemoveBackground(false); }
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo leer la imagen.", "error");
    }
  }

  function updateArtwork<K extends keyof ProfileArtworkPlacement>(key: K, value: ProfileArtworkPlacement[K]) {
    setArtwork((current) => ({ ...current, [key]: value }));
  }

  async function save() {
    if (!name.trim()) return onNotice("El perfil necesita un nombre.", "error");
    setSaving(true);
    try {
      const updated = await updateProfile({
        id: profile.id,
        name: name.trim(),
        description: description.trim(),
        iconDataUrl,
        backgroundDataUrl,
        removeIcon,
        removeBackground,
      });
      const placement = await updateArtworkPlacement(profile.id, artwork);
      const hydrated = { ...updated, artwork: placement.artwork };
      setEditing(false);
      setIconDataUrl(null);
      setBackgroundDataUrl(null);
      setRemoveIcon(false);
      setRemoveBackground(false);
      onUpdated(hydrated);
      onNotice("Perfil y encuadre actualizados.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo guardar el perfil.", "error");
    } finally {
      setSaving(false);
    }
  }

  async function remove() {
    setDeleting(true);
    try {
      await deleteProfile(profile.id);
      setConfirmDelete(false);
      onDeleted();
      onNotice("Perfil eliminado.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo eliminar el perfil.", "error");
    } finally {
      setDeleting(false);
    }
  }

  async function activateBoost() {
    if (launching || boostBusy) return;
    setBoostBusy(true);
    setBoostSummary(null);
    try {
      const result = await applyBoost(profile.id);
      setBoostSummary(result);
      setBoostStatus(await getBoostStatus(profile.id));
      onNotice(result.reapplied ? "NEXA Boost Equilibrado reaplicado." : "NEXA Boost activado.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo aplicar NEXA Boost.", "error");
    } finally {
      setBoostBusy(false);
    }
  }

  async function deactivateBoost() {
    if (launching || boostBusy) return;
    setBoostBusy(true);
    try {
      const result = await removeBoost(profile.id);
      setConfirmRemoveBoost(false);
      setBoostSummary(null);
      setBoostStatus(await getBoostStatus(profile.id));
      const preserved = result.preserved.length ? ` · ${result.preserved.length} cambio(s) del usuario preservado(s)` : "";
      onNotice(`NEXA Boost desactivado${preserved}.`, "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo desactivar NEXA Boost.", "error");
    } finally {
      setBoostBusy(false);
    }
  }

  return (
    <section className="page profile-detail-page">
      <button className="text-back" type="button" onClick={onBack}>← Biblioteca</button>
      <div className="profile-hero glass-panel" style={heroStyle}>
        <div className="profile-hero-icon"><img src={shownIcon} alt="" style={iconStyle} /></div>
        <div className="profile-hero-copy">
          <span className="eyebrow">PERFIL NEXA</span>
          <h1>{profile.name}</h1>
          <p>{profile.description || "Sin descripción."}</p>
          <div className="profile-meta"><span>{profile.loader}{profile.loaderVersion ? ` ${profile.loaderVersion}` : ""}</span><span>Minecraft {profile.minecraftVersion}</span>{profile.memoryMiB && <span>{profile.memoryMiB} MB RAM</span>}{boostStatus?.applied && <span className="boost-meta"><Sparkles size={12} /> BOOST</span>}</div>
        </div>
        <div className="profile-hero-actions">
          <button className="primary-button wide-action" type="button" disabled={launching} onClick={() => onLaunch(profile)}>{launching ? <Loader2 className="spin" size={17} /> : <Play size={17} fill="currentColor" />} INICIAR</button>
          <button className={`secondary-button ${boostStatus?.applied ? "boost-active-button" : ""}`} type="button" onClick={() => setBoostOpen((value) => !value)}><Gauge size={16} /> NEXA BOOST</button>
          <button className="secondary-button" type="button" onClick={() => onContent(profile)}><Boxes size={16} /> CONTENIDO</button>
          <button className="secondary-button" type="button" onClick={() => setEditing((value) => !value)}>{editing ? <X size={16} /> : <ImagePlus size={16} />} {editing ? "CERRAR EDITOR" : "EDITAR PERFIL"}</button>
          <button className="ghost-button" type="button" onClick={() => openProfileFolder(profile.id).catch((error: Error) => onNotice(error.message, "error"))}><FolderOpen size={16} /> ABRIR CARPETA</button>
        </div>
      </div>

      {boostOpen && (
        <section className="boost-panel glass-panel">
          <div className="boost-heading">
            <div><span className="eyebrow">RENDIMIENTO ADMINISTRADO</span><h2>NEXA Boost · Equilibrado</h2><p>Instala sólo builds compatibles y conserva gráficos, feedback de combate y cambios manuales del usuario.</p></div>
            <span className={`boost-status ${boostStatus?.applied ? "active" : ""}`}>{boostStatus?.applied ? <><Check size={14} /> ACTIVO</> : "INACTIVO"}</span>
          </div>

          {!boostStatus ? <div className="boost-loading"><Loader2 className="spin" size={17} /> Comprobando perfil…</div> : !boostStatus.supported ? (
            <div className="boost-unsupported">NEXA Boost no convierte perfiles Vanilla automáticamente. Crea un perfil Fabric, Forge o NeoForge para usarlo.</div>
          ) : (
            <>
              <div className="boost-components">
                {boostStatus.components.map((component) => <article key={component.id}><Sparkles size={15} /><div><strong>{component.name}</strong><span>{component.purpose}</span></div></article>)}
                <article><Sparkles size={15} /><div><strong>Particle Core</strong><span>Reduce selectivamente partículas ambientales sin borrar feedback de combate.</span></div></article>
              </div>
              <div className="boost-note">En Fabric/NeoForge, Sodium Extra forma parte del conjunto cuando existe una build compatible. Los presets de NEXA In-Game también coordinan sus opciones ambientales sin tocar VSync, resolución, shaders ni niebla protegida.</div>
              {boostSummary && (
                <div className="boost-result">
                  <strong>{boostSummary.reapplied ? "Preset actualizado" : `${boostSummary.filesInstalled} archivo(s) administrado(s) instalado(s)`}</strong>
                  {boostSummary.presetChanges.length > 0 && <span>{boostSummary.presetChanges.slice(0, 3).join(" · ")}</span>}
                  {boostSummary.note && <span>{boostSummary.note}</span>}
                </div>
              )}
              <div className="boost-actions">
                <button className="primary-button" type="button" disabled={boostBusy || launching} onClick={activateBoost}>{boostBusy ? <Loader2 className="spin" size={16} /> : <Gauge size={16} />} {boostStatus.applied ? "REAPLICAR EQUILIBRADO" : "ACTIVAR BOOST"}</button>
                {boostStatus.applied && <button className="ghost-button danger-text" type="button" disabled={boostBusy || launching} onClick={() => setConfirmRemoveBoost(true)}>DESACTIVAR</button>}
              </div>
            </>
          )}
        </section>
      )}

      {editing && (
        <div className="profile-editor glass-panel">
          <div className="editor-heading"><div><span className="eyebrow">PERSONALIZACIÓN</span><h2>Editar perfil</h2></div><button className="primary-button" type="button" disabled={saving} onClick={save}>{saving ? <Loader2 className="spin" size={16} /> : <Save size={16} />} GUARDAR CAMBIOS</button></div>
          <div className="editor-grid">
            <div className="editor-fields">
              <label className="field-label">NOMBRE<input className="nexa-input" maxLength={64} value={name} onChange={(event) => setName(event.target.value)} /></label>
              <label className="field-label">DESCRIPCIÓN<textarea className="nexa-input nexa-textarea" maxLength={800} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
              <div className="artwork-help"><strong>Encuadre libre</strong><span>Mueve X/Y hasta dejar el rostro o elemento importante donde quieras. “Recortar” llena el recuadro; “Completa” conserva la imagen entera.</span></div>
              <div className="danger-zone"><div><strong>Eliminar perfil</strong><span>Borra sólo este GUID y su contenido privado.</span></div><button className="danger-button" type="button" onClick={() => setConfirmDelete(true)}><Trash2 size={15} /> ELIMINAR</button></div>
            </div>
            <div className="editor-artwork">
              <div className="artwork-card compact-artwork">
                <span>ICONO</span>
                <div className="icon-preview"><img src={shownIcon} alt="" style={iconStyle} /></div>
                <div className="artwork-actions"><button className="secondary-button" type="button" onClick={() => iconInput.current?.click()}>CAMBIAR</button><button className="ghost-button" type="button" onClick={() => { setIconDataUrl(null); setRemoveIcon(true); }}>RESTABLECER</button></div>
                <div className="fit-toggle"><button className={artwork.iconFit === "cover" ? "active" : ""} type="button" onClick={() => updateArtwork("iconFit", "cover")}>RECORTAR</button><button className={artwork.iconFit === "contain" ? "active" : ""} type="button" onClick={() => updateArtwork("iconFit", "contain")}>COMPLETA</button></div>
                <PositionControls x={artwork.iconPositionX} y={artwork.iconPositionY} onX={(value) => updateArtwork("iconPositionX", value)} onY={(value) => updateArtwork("iconPositionY", value)} onCenter={() => setArtwork((current) => ({ ...current, iconPositionX: 50, iconPositionY: 50 }))} />
                <input hidden ref={iconInput} type="file" accept="image/*" onChange={(event) => choose("icon", event.target.files?.[0])} />
              </div>
              <div className="artwork-card compact-artwork wide">
                <span>FONDO</span>
                <div className="background-preview" style={shownBackground ? { backgroundImage: `url(${shownBackground})`, backgroundPosition: `${artwork.backgroundPositionX}% ${artwork.backgroundPositionY}%`, backgroundSize: artwork.backgroundFit, backgroundRepeat: "no-repeat" } : undefined} />
                <div className="artwork-actions"><button className="secondary-button" type="button" onClick={() => backgroundInput.current?.click()}>CAMBIAR</button><button className="ghost-button" type="button" onClick={() => { setBackgroundDataUrl(null); setRemoveBackground(true); }}>QUITAR</button></div>
                <div className="fit-toggle"><button className={artwork.backgroundFit === "cover" ? "active" : ""} type="button" onClick={() => updateArtwork("backgroundFit", "cover")}>RECORTAR</button><button className={artwork.backgroundFit === "contain" ? "active" : ""} type="button" onClick={() => updateArtwork("backgroundFit", "contain")}>COMPLETA</button></div>
                <PositionControls x={artwork.backgroundPositionX} y={artwork.backgroundPositionY} onX={(value) => updateArtwork("backgroundPositionX", value)} onY={(value) => updateArtwork("backgroundPositionY", value)} onCenter={() => setArtwork((current) => ({ ...current, backgroundPositionX: 50, backgroundPositionY: 50 }))} />
                <input hidden ref={backgroundInput} type="file" accept="image/*" onChange={(event) => choose("background", event.target.files?.[0])} />
              </div>
            </div>
          </div>
        </div>
      )}

      <NexaDialog open={confirmDelete} tone="danger" title="Eliminar perfil" description={`Se eliminará '${profile.name}' con sus mundos, mods y configuración. Los recursos compartidos de NEXA y los demás perfiles no se tocarán.`} confirmLabel="ELIMINAR" busy={deleting} onCancel={() => setConfirmDelete(false)} onConfirm={remove} />
      <NexaDialog open={confirmRemoveBoost} tone="danger" title="Desactivar NEXA Boost" description="NEXA restaurará sólo los valores que sigan con el valor aplicado por Boost y eliminará únicamente JARs administrados cuyo hash siga intacto. Los cambios manuales se preservan." confirmLabel="DESACTIVAR" busy={boostBusy} onCancel={() => setConfirmRemoveBoost(false)} onConfirm={deactivateBoost} />
    </section>
  );
}

function PositionControls({ x, y, onX, onY, onCenter }: { x: number; y: number; onX(value: number): void; onY(value: number): void; onCenter(): void }) {
  return (
    <div className="position-controls">
      <label><span>Horizontal <b>{Math.round(x)}%</b></span><input type="range" min="0" max="100" value={x} onChange={(event) => onX(Number(event.target.value))} /></label>
      <label><span>Vertical <b>{Math.round(y)}%</b></span><input type="range" min="0" max="100" value={y} onChange={(event) => onY(Number(event.target.value))} /></label>
      <button className="center-artwork" type="button" onClick={onCenter}><RotateCcw size={13} /> CENTRAR</button>
    </div>
  );
}
