import { useRef, useState } from "react";
import { Boxes, FolderOpen, ImagePlus, Loader2, Play, Save, Trash2, X } from "lucide-react";
import { deleteProfile, openProfileFolder, updateProfile } from "../app/nexa-bridge";
import type { NexaProfile } from "../app/types";

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
  const [saving, setSaving] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const iconInput = useRef<HTMLInputElement>(null);
  const backgroundInput = useRef<HTMLInputElement>(null);

  const shownIcon = removeIcon ? "./brand/nexa-mark.png" : iconDataUrl ?? profile.iconDataUrl ?? "./brand/nexa-mark.png";
  const shownBackground = removeBackground ? null : backgroundDataUrl ?? profile.backgroundDataUrl ?? null;

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
      setEditing(false);
      setIconDataUrl(null);
      setBackgroundDataUrl(null);
      setRemoveIcon(false);
      setRemoveBackground(false);
      onUpdated(updated);
      onNotice("Perfil actualizado.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo guardar el perfil.", "error");
    } finally {
      setSaving(false);
    }
  }

  async function remove() {
    if (!window.confirm(`¿Eliminar definitivamente '${profile.name}'?\n\nSe borrarán mundos, mods y configuración de este perfil. Los recursos compartidos y otros perfiles no se tocarán.`)) return;
    setDeleting(true);
    try {
      await deleteProfile(profile.id);
      onDeleted();
      onNotice("Perfil eliminado.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo eliminar el perfil.", "error");
    } finally {
      setDeleting(false);
    }
  }

  return (
    <section className="page profile-detail-page">
      <button className="text-back" type="button" onClick={onBack}>← Biblioteca</button>
      <div className="profile-hero glass-panel" style={shownBackground ? { backgroundImage: `linear-gradient(90deg,rgba(6,10,17,.97) 0%,rgba(6,10,17,.72) 48%,rgba(6,10,17,.30) 100%),url(${shownBackground})` } : undefined}>
        <div className="profile-hero-icon"><img src={shownIcon} alt="" /></div>
        <div className="profile-hero-copy">
          <span className="eyebrow">PERFIL NEXA</span>
          <h1>{profile.name}</h1>
          <p>{profile.description || "Sin descripción."}</p>
          <div className="profile-meta"><span>{profile.loader}{profile.loaderVersion ? ` ${profile.loaderVersion}` : ""}</span><span>Minecraft {profile.minecraftVersion}</span>{profile.memoryMiB && <span>{profile.memoryMiB} MB RAM</span>}</div>
        </div>
        <div className="profile-hero-actions">
          <button className="primary-button wide-action" type="button" disabled={launching} onClick={() => onLaunch(profile)}>{launching ? <Loader2 className="spin" size={17} /> : <Play size={17} fill="currentColor" />} INICIAR</button>
          <button className="secondary-button" type="button" onClick={() => onContent(profile)}><Boxes size={16} /> CONTENIDO</button>
          <button className="secondary-button" type="button" onClick={() => setEditing((value) => !value)}>{editing ? <X size={16} /> : <ImagePlus size={16} />} {editing ? "CERRAR EDITOR" : "EDITAR PERFIL"}</button>
          <button className="ghost-button" type="button" onClick={() => openProfileFolder(profile.id).catch((error: Error) => onNotice(error.message, "error"))}><FolderOpen size={16} /> ABRIR CARPETA</button>
        </div>
      </div>

      {editing && (
        <div className="profile-editor glass-panel">
          <div className="editor-heading"><div><span className="eyebrow">PERSONALIZACIÓN</span><h2>Editar perfil</h2></div><button className="primary-button" type="button" disabled={saving} onClick={save}>{saving ? <Loader2 className="spin" size={16} /> : <Save size={16} />} GUARDAR CAMBIOS</button></div>
          <div className="editor-grid">
            <div className="editor-fields">
              <label className="field-label">NOMBRE<input className="nexa-input" maxLength={64} value={name} onChange={(event) => setName(event.target.value)} /></label>
              <label className="field-label">DESCRIPCIÓN<textarea className="nexa-input nexa-textarea" maxLength={800} value={description} onChange={(event) => setDescription(event.target.value)} /></label>
              <div className="danger-zone"><div><strong>Eliminar perfil</strong><span>Borra sólo este GUID y su contenido privado.</span></div><button className="danger-button" type="button" disabled={deleting} onClick={remove}>{deleting ? <Loader2 className="spin" size={15} /> : <Trash2 size={15} />} ELIMINAR</button></div>
            </div>
            <div className="editor-artwork">
              <div className="artwork-card compact-artwork"><span>ICONO</span><div className="icon-preview"><img src={shownIcon} alt="" /></div><div className="artwork-actions"><button className="secondary-button" type="button" onClick={() => iconInput.current?.click()}>CAMBIAR</button><button className="ghost-button" type="button" onClick={() => { setIconDataUrl(null); setRemoveIcon(true); }}>RESTABLECER</button></div><input hidden ref={iconInput} type="file" accept="image/*" onChange={(event) => choose("icon", event.target.files?.[0])} /></div>
              <div className="artwork-card compact-artwork wide"><span>FONDO</span><div className="background-preview" style={shownBackground ? { backgroundImage: `url(${shownBackground})` } : undefined} /><div className="artwork-actions"><button className="secondary-button" type="button" onClick={() => backgroundInput.current?.click()}>CAMBIAR</button><button className="ghost-button" type="button" onClick={() => { setBackgroundDataUrl(null); setRemoveBackground(true); }}>QUITAR</button></div><input hidden ref={backgroundInput} type="file" accept="image/*" onChange={(event) => choose("background", event.target.files?.[0])} /></div>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
