import { useEffect, useMemo, useState } from "react";
import { Boxes, Download, ExternalLink, Loader2, PackagePlus, Search as SearchIcon, ToggleLeft, ToggleRight, Trash2, X } from "lucide-react";
import {
  deleteInstalledContent,
  installContent,
  listInstalledContent,
  openInstalledContent,
  searchContent,
  toggleInstalledContent,
} from "../app/nexa-bridge";
import type { ContentCatalogProject, InstalledContentEntry, NexaProfile } from "../app/types";
import { NexaDialog } from "../components/NexaDialog";

type Props = {
  profiles: NexaProfile[];
  initialProfileId?: string | null;
  onSelectProfile(id: string): void;
  onNotice(message: string, kind?: "success" | "error"): void;
};

type Mode = "installed" | "catalog";
type ProjectType = ContentCatalogProject["projectType"];

const typeLabels: Record<ProjectType, string> = {
  mod: "Mods",
  resourcepack: "Texturas",
  shader: "Shaders",
  datapack: "Datapacks",
};

function formatSize(bytes: number) {
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
  if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(2)} MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${bytes} B`;
}

export function ContentPage({ profiles, initialProfileId, onSelectProfile, onNotice }: Props) {
  const [profileId, setProfileId] = useState(initialProfileId ?? profiles[0]?.id ?? "");
  const [mode, setMode] = useState<Mode>("installed");
  const [installed, setInstalled] = useState<InstalledContentEntry[]>([]);
  const [filter, setFilter] = useState("");
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState("");
  const [projectType, setProjectType] = useState<ProjectType>("mod");
  const [results, setResults] = useState<ContentCatalogProject[]>([]);
  const [searching, setSearching] = useState(false);
  const [installingId, setInstallingId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<InstalledContentEntry | null>(null);
  const selected = profiles.find((profile) => profile.id === profileId) ?? profiles[0];

  useEffect(() => {
    if (!profileId && profiles[0]) setProfileId(profiles[0].id);
  }, [profileId, profiles]);

  useEffect(() => {
    if (!profileId) return;
    onSelectProfile(profileId);
    setLoading(true);
    listInstalledContent(profileId)
      .then(setInstalled)
      .catch((error: Error) => onNotice(error.message, "error"))
      .finally(() => setLoading(false));
  }, [profileId, onNotice, onSelectProfile]);

  const filtered = useMemo(() => {
    const value = filter.trim().toLowerCase();
    return value ? installed.filter((entry) => `${entry.name} ${entry.category}`.toLowerCase().includes(value)) : installed;
  }, [filter, installed]);

  const groups = useMemo(() => Array.from(new Set(filtered.map((entry) => entry.category))).map((category) => ({
    category,
    entries: filtered.filter((entry) => entry.category === category),
  })), [filtered]);

  async function refresh() {
    if (!profileId) return;
    setInstalled(await listInstalledContent(profileId));
  }

  async function toggle(entry: InstalledContentEntry) {
    try {
      await toggleInstalledContent(profileId, entry);
      await refresh();
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo cambiar el mod.", "error");
    }
  }

  async function remove() {
    if (!deleteTarget) return;
    try {
      await deleteInstalledContent(profileId, deleteTarget);
      setDeleteTarget(null);
      await refresh();
      onNotice("Contenido eliminado del perfil.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo eliminar el contenido.", "error");
    }
  }

  async function runSearch() {
    if (!profileId) return;
    setSearching(true);
    try {
      setResults(await searchContent(profileId, query.trim(), projectType));
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo consultar el catálogo.", "error");
    } finally {
      setSearching(false);
    }
  }

  async function install(project: ContentCatalogProject) {
    setInstallingId(project.id);
    try {
      const response = await installContent(profileId, project);
      setInstalled(response.installed);
      onNotice(`${project.title}: ${response.filesInstalled} archivo(s) instalado(s).`, "success");
      setMode("installed");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo instalar el contenido.", "error");
    } finally {
      setInstallingId(null);
    }
  }

  if (!selected) {
    return <section className="page"><div className="empty-state glass-panel"><h2>No hay perfiles</h2><p>Crea un perfil antes de administrar contenido.</p></div></section>;
  }

  return (
    <section className="page content-page">
      <div className="content-heading">
        <div><span className="eyebrow">CONTENIDO DEL PERFIL</span><h1>{selected.name}</h1><p>{selected.loader} · Minecraft {selected.minecraftVersion}</p></div>
        <div className="content-heading-actions">
          <select className="nexa-input nexa-select profile-select" value={selected.id} onChange={(event) => { setProfileId(event.target.value); setMode("installed"); }}>{profiles.map((profile) => <option key={profile.id} value={profile.id}>{profile.name}</option>)}</select>
          {mode === "installed" ? <button className="primary-button" type="button" onClick={() => setMode("catalog")}><PackagePlus size={17} /> AGREGAR CONTENIDO</button> : <button className="secondary-button" type="button" onClick={() => setMode("installed")}><X size={16} /> VOLVER A INSTALADO</button>}
        </div>
      </div>

      <div className="content-tabs">
        <button className={mode === "installed" ? "active" : ""} type="button" onClick={() => setMode("installed")}>INSTALADO <span>{installed.length}</span></button>
        <button className={mode === "catalog" ? "active" : ""} type="button" onClick={() => setMode("catalog")}>CATÁLOGO</button>
      </div>

      {mode === "installed" ? (
        <div className="installed-layout">
          <div className="installed-toolbar"><div className="search-field"><SearchIcon size={16} /><input value={filter} onChange={(event) => setFilter(event.target.value)} placeholder="Filtrar contenido instalado..." /></div><span>{filtered.length} elementos</span></div>
          {loading ? <div className="loading-panel glass-panel"><Loader2 className="spin" /> Leyendo contenido del perfil…</div> : groups.length === 0 ? (
            <div className="empty-state glass-panel"><Boxes size={28} /><h2>Este perfil todavía está limpio</h2><p>Los mods, texturas, shaders y datapacks que instales aparecerán aquí primero.</p><button className="primary-button" type="button" onClick={() => setMode("catalog")}><PackagePlus size={16} /> AGREGAR CONTENIDO</button></div>
          ) : groups.map((group) => (
            <section className="content-group" key={group.category}>
              <header><strong>{group.category}</strong><span>{group.entries.length}</span></header>
              <div className="installed-list">
                {group.entries.map((entry) => (
                  <article className="installed-row glass-panel" key={entry.relativePath}>
                    <div className="installed-icon"><Boxes size={18} /></div>
                    <div className="installed-copy"><strong>{entry.name}</strong><span>{entry.isDirectory ? "Carpeta" : `${formatSize(entry.sizeBytes)} · ${entry.relativePath}`}</span></div>
                    <div className="installed-actions">
                      {entry.canToggle && <button className={`state-button ${entry.enabled ? "enabled" : ""}`} type="button" onClick={() => toggle(entry)}>{entry.enabled ? <ToggleRight size={18} /> : <ToggleLeft size={18} />}{entry.enabled ? "ACTIVO" : "DESACTIVADO"}</button>}
                      <button className="icon-text-button" type="button" onClick={() => openInstalledContent(profileId, entry).catch((error: Error) => onNotice(error.message, "error"))}><ExternalLink size={15} /> ABRIR</button>
                      <button className="icon-text-button danger-text" type="button" onClick={() => setDeleteTarget(entry)}><Trash2 size={15} /> ELIMINAR</button>
                    </div>
                  </article>
                ))}
              </div>
            </section>
          ))}
        </div>
      ) : (
        <div className="catalog-layout">
          <div className="catalog-controls glass-panel">
            <div className="search-field catalog-search"><SearchIcon size={17} /><input value={query} onChange={(event) => setQuery(event.target.value)} onKeyDown={(event) => event.key === "Enter" && runSearch()} placeholder="Buscar en Modrinth..." /></div>
            <select className="nexa-input nexa-select" value={projectType} onChange={(event) => setProjectType(event.target.value as ProjectType)}>{Object.entries(typeLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select>
            <button className="primary-button" type="button" disabled={searching} onClick={runSearch}>{searching ? <Loader2 className="spin" size={16} /> : <SearchIcon size={16} />} BUSCAR</button>
          </div>
          <p className="catalog-note">NEXA sólo muestra resultados compatibles con Minecraft {selected.minecraftVersion}{projectType === "mod" ? ` y ${selected.loader}` : ""}.</p>
          <div className="catalog-results">
            {results.map((project) => (
              <article className="catalog-row glass-panel" key={project.id}>
                <div className="catalog-icon">{project.iconUrl ? <img src={project.iconUrl} alt="" /> : <Boxes size={22} />}</div>
                <div className="catalog-copy"><div><strong>{project.title}</strong><span>por {project.author}</span></div><p>{project.description}</p><small>{project.downloads.toLocaleString()} descargas · {typeLabels[project.projectType]}</small></div>
                <button className="secondary-button" type="button" disabled={installingId === project.id} onClick={() => install(project)}>{installingId === project.id ? <Loader2 className="spin" size={16} /> : <Download size={16} />} INSTALAR</button>
              </article>
            ))}
            {!searching && results.length === 0 && <div className="catalog-empty"><PackagePlus size={26} /><strong>Busca contenido para {selected.name}</strong><span>Los resultados compatibles aparecerán aquí.</span></div>}
          </div>
        </div>
      )}

      <NexaDialog
        open={Boolean(deleteTarget)}
        tone="danger"
        title="Eliminar contenido"
        description={deleteTarget ? `Se eliminará '${deleteTarget.name}' únicamente de ${selected.name}. Los demás perfiles y los recursos compartidos de NEXA no se tocarán.` : ""}
        confirmLabel="ELIMINAR"
        onCancel={() => setDeleteTarget(null)}
        onConfirm={remove}
      />
    </section>
  );
}
