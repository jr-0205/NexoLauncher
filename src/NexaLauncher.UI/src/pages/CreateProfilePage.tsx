import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, ArrowRight, Check, Cpu, ImagePlus, Loader2, RotateCcw, Sparkles } from "lucide-react";
import { createProfile, getLoaderVersions, getMinecraftVersions } from "../app/nexa-bridge";
import type { LoaderVersionItem, MinecraftVersionItem, NexaProfile } from "../app/types";

type LoaderName = "Vanilla" | "Fabric" | "Forge" | "NeoForge";

type Props = {
  onCreated(profile: NexaProfile): void;
  onCancel(): void;
  onNotice(message: string, kind?: "success" | "error"): void;
};

const loaders: LoaderName[] = ["Vanilla", "Fabric", "Forge", "NeoForge"];

async function imageToDataUrl(file: File) {
  if (file.size > 8 * 1024 * 1024) throw new Error("La imagen no puede superar 8 MB.");
  if (!file.type.startsWith("image/")) throw new Error("Selecciona un archivo de imagen válido.");
  return await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error("No se pudo leer la imagen."));
    reader.readAsDataURL(file);
  });
}

export function CreateProfilePage({ onCreated, onCancel, onNotice }: Props) {
  const [step, setStep] = useState(1);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [versions, setVersions] = useState<MinecraftVersionItem[]>([]);
  const [versionQuery, setVersionQuery] = useState("");
  const [minecraftVersion, setMinecraftVersion] = useState("");
  const [loader, setLoader] = useState<LoaderName>("Vanilla");
  const [loaderVersions, setLoaderVersions] = useState<LoaderVersionItem[]>([]);
  const [loaderVersion, setLoaderVersion] = useState<string | null>(null);
  const [iconDataUrl, setIconDataUrl] = useState<string | null>(null);
  const [backgroundDataUrl, setBackgroundDataUrl] = useState<string | null>(null);
  const [loadingVersions, setLoadingVersions] = useState(true);
  const [loadingLoader, setLoadingLoader] = useState(false);
  const [creating, setCreating] = useState(false);
  const iconInput = useRef<HTMLInputElement>(null);
  const backgroundInput = useRef<HTMLInputElement>(null);

  useEffect(() => {
    getMinecraftVersions()
      .then((items) => {
        setVersions(items);
        setMinecraftVersion((current) => current || items[0]?.id || "");
      })
      .catch((error: Error) => onNotice(error.message, "error"))
      .finally(() => setLoadingVersions(false));
  }, [onNotice]);

  useEffect(() => {
    if (!minecraftVersion || loader === "Vanilla") {
      setLoaderVersions([]);
      setLoaderVersion(null);
      return;
    }
    setLoadingLoader(true);
    getLoaderVersions(minecraftVersion, loader)
      .then((items) => {
        setLoaderVersions(items);
        setLoaderVersion(items.find((item) => item.stable)?.version ?? items[0]?.version ?? null);
      })
      .catch((error: Error) => {
        setLoaderVersions([]);
        setLoaderVersion(null);
        onNotice(error.message, "error");
      })
      .finally(() => setLoadingLoader(false));
  }, [minecraftVersion, loader, onNotice]);

  const visibleVersions = useMemo(() => {
    const query = versionQuery.trim().toLowerCase();
    return (query ? versions.filter((item) => item.id.toLowerCase().includes(query)) : versions).slice(0, 80);
  }, [versionQuery, versions]);

  const canContinue = step === 1
    ? name.trim().length > 0
    : step === 2
      ? Boolean(minecraftVersion) && (loader === "Vanilla" || Boolean(loaderVersion))
      : true;

  async function chooseArtwork(kind: "icon" | "background", file?: File) {
    if (!file) return;
    try {
      const dataUrl = await imageToDataUrl(file);
      if (kind === "icon") setIconDataUrl(dataUrl);
      else setBackgroundDataUrl(dataUrl);
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo leer la imagen.", "error");
    }
  }

  async function finish() {
    if (!canContinue || creating) return;
    setCreating(true);
    try {
      const profile = await createProfile({
        name: name.trim(),
        description: description.trim(),
        minecraftVersion,
        loader,
        loaderVersion,
        iconDataUrl,
        backgroundDataUrl,
      });
      onNotice(`${profile.name} quedó listo.`, "success");
      onCreated(profile);
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo crear el perfil.", "error");
    } finally {
      setCreating(false);
    }
  }

  return (
    <section className="page create-page">
      <div className="wizard-shell glass-panel">
        <aside className="wizard-rail">
          <div>
            <span className="eyebrow">CREAR PERFIL</span>
            <h2>Nuevo espacio</h2>
            <p>Cada perfil conserva sus mundos, mods y ajustes por separado.</p>
          </div>
          <div className="wizard-steps">
            {[1, 2, 3].map((value) => (
              <button key={value} type="button" className={`wizard-step ${step === value ? "active" : ""} ${step > value ? "done" : ""}`} onClick={() => value < step && setStep(value)}>
                <span>{step > value ? <Check size={15} /> : value}</span>
                <div><strong>{value === 1 ? "Información" : value === 2 ? "Versión del juego" : "Apariencia"}</strong><small>{value === 1 ? "Nombre y descripción" : value === 2 ? "Minecraft y loader" : "Icono y fondo"}</small></div>
              </button>
            ))}
          </div>
          <button className="ghost-button wizard-cancel" type="button" onClick={onCancel}>CANCELAR</button>
        </aside>

        <div className="wizard-content">
          {step === 1 && (
            <div className="wizard-stage">
              <span className="eyebrow">PASO 1 DE 3</span>
              <h1>Información del perfil</h1>
              <p className="stage-description">El nombre pertenece al usuario. NEXA no lo cambiará cuando selecciones otra versión o loader.</p>
              <label className="field-label">NOMBRE DEL PERFIL<input className="nexa-input" autoFocus maxLength={64} value={name} onChange={(event) => setName(event.target.value)} placeholder="Ej. Diosesmon, Survival, Fabric PvP..." /></label>
              <label className="field-label">DESCRIPCIÓN<textarea className="nexa-input nexa-textarea" maxLength={800} value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Opcional. Describe para qué usarás este perfil." /></label>
              <div className="info-strip"><Sparkles size={17} /><div><strong>Perfil aislado por diseño</strong><span>El nombre visible nunca determina la carpeta física. NEXA mantiene un GUID independiente.</span></div></div>
            </div>
          )}

          {step === 2 && (
            <div className="wizard-stage version-stage">
              <span className="eyebrow">PASO 2 DE 3</span>
              <h1>Seleccionar versión del juego</h1>
              <p className="stage-description">NEXA instalará los archivos compartidos y resolverá Java automáticamente.</p>
              <div className="loader-tabs">
                {loaders.map((value) => <button type="button" key={value} className={loader === value ? "active" : ""} onClick={() => setLoader(value)}>{value}</button>)}
              </div>
              <div className="version-layout">
                <div className="version-browser">
                  <input className="nexa-input" value={versionQuery} onChange={(event) => setVersionQuery(event.target.value)} placeholder="Buscar versión..." />
                  <div className="version-list">
                    {loadingVersions ? <div className="loading-row"><Loader2 className="spin" size={18} /> Consultando Mojang…</div> : visibleVersions.map((item) => (
                      <button type="button" key={item.id} className={`version-row ${minecraftVersion === item.id ? "active" : ""}`} onClick={() => setMinecraftVersion(item.id)}>
                        <div><strong>{item.id}</strong><small>Publicada {new Date(item.releaseTime).toLocaleDateString()}</small></div><span>ESTABLE</span>
                      </button>
                    ))}
                  </div>
                </div>
                <div className="version-summary glass-panel">
                  <span className="eyebrow">CONFIGURACIÓN</span>
                  <h3>Minecraft {minecraftVersion || "—"}</h3>
                  <p>{loader}</p>
                  {loader !== "Vanilla" && <label className="field-label">VERSIÓN DEL LOADER<select className="nexa-input nexa-select" value={loaderVersion ?? ""} disabled={loadingLoader || loaderVersions.length === 0} onChange={(event) => setLoaderVersion(event.target.value)}>{loaderVersions.map((item) => <option key={item.version} value={item.version}>{item.version}{item.stable ? " · estable" : ""}</option>)}</select></label>}
                  {loadingLoader && <div className="subtle-status"><Loader2 className="spin" size={14} /> Consultando {loader}…</div>}
                  <div className="java-auto"><Cpu size={18} /><div><strong>JAVA AUTOMÁTICO</strong><span>NEXA elegirá el runtime compatible al instalar y jugar.</span></div></div>
                </div>
              </div>
            </div>
          )}

          {step === 3 && (
            <div className="wizard-stage">
              <span className="eyebrow">PASO 3 DE 3</span>
              <h1>Apariencia</h1>
              <p className="stage-description">Personaliza la tarjeta del perfil. Si no eliges icono, se utilizará la N de NEXA.</p>
              <div className="artwork-grid">
                <div className="artwork-card">
                  <span>ICONO</span>
                  <div className="icon-preview"><img src={iconDataUrl ?? "./brand/nexa-mark.png"} alt="Vista previa del icono" /></div>
                  <div className="artwork-actions"><button className="secondary-button" type="button" onClick={() => iconInput.current?.click()}><ImagePlus size={16} /> ELEGIR ICONO</button>{iconDataUrl && <button className="ghost-button" type="button" onClick={() => setIconDataUrl(null)}><RotateCcw size={15} /> RESTABLECER</button>}</div>
                  <input ref={iconInput} hidden type="file" accept="image/png,image/jpeg,image/webp,image/bmp" onChange={(event) => chooseArtwork("icon", event.target.files?.[0])} />
                </div>
                <div className="artwork-card background-artwork">
                  <span>FONDO</span>
                  <div className="background-preview" style={backgroundDataUrl ? { backgroundImage: `linear-gradient(rgba(4,8,14,.12),rgba(4,8,14,.42)),url(${backgroundDataUrl})` } : undefined}><div className="preview-watermark"><img src={iconDataUrl ?? "./brand/nexa-mark.png"} alt="" /><strong>{name.trim() || "TU PERFIL"}</strong></div></div>
                  <div className="artwork-actions"><button className="secondary-button" type="button" onClick={() => backgroundInput.current?.click()}><ImagePlus size={16} /> ELEGIR FONDO</button>{backgroundDataUrl && <button className="ghost-button" type="button" onClick={() => setBackgroundDataUrl(null)}>QUITAR FONDO</button>}</div>
                  <input ref={backgroundInput} hidden type="file" accept="image/png,image/jpeg,image/webp,image/bmp" onChange={(event) => chooseArtwork("background", event.target.files?.[0])} />
                </div>
              </div>
            </div>
          )}

          <footer className="wizard-footer">
            <span>Paso {step} de 3</span>
            <div>
              {step > 1 && <button className="secondary-button" type="button" disabled={creating} onClick={() => setStep((value) => value - 1)}><ArrowLeft size={16} /> ATRÁS</button>}
              {step < 3 ? <button className="primary-button" type="button" disabled={!canContinue} onClick={() => setStep((value) => value + 1)}>SIGUIENTE <ArrowRight size={16} /></button> : <button className="primary-button" type="button" disabled={creating || !canContinue} onClick={finish}>{creating ? <Loader2 className="spin" size={17} /> : <Check size={17} />} CREAR PERFIL</button>}
            </div>
          </footer>
        </div>
      </div>
    </section>
  );
}
