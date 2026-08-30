import { type ReactNode, useEffect, useMemo, useState } from "react";
import { Archive, Check, ChevronDown, ChevronRight, ExternalLink, FolderOpen, Hammer, Layers3, Loader2, RefreshCw, Save, ShieldCheck } from "lucide-react";
import {
  generateNexaInGameBuild,
  getMinecraftVersions,
  getNexaInGameBuildLibrary,
  isNativeHost,
  openNexaInGameBuildFolder,
  updateSettings,
} from "../app/nexa-bridge";
import type { MinecraftVersionItem, NexaInGameBuildEntry, NexaInGameBuildLibrary } from "../app/types";
import { nexaWordmarkDataUrl } from "../brand/nexa-wordmark";
import { NexaDialog } from "../components/NexaDialog";

type Props = {
  username: string;
  closeLauncherOnGameStart: boolean;
  version: string;
  onUpdated(username: string, closeLauncherOnGameStart: boolean): void;
  onNotice(message: string, kind?: "success" | "error"): void;
};

type BuildFamily = {
  id: string;
  rows: NexaInGameBuildEntry[];
};

export function SettingsPage({ username, closeLauncherOnGameStart, version, onUpdated, onNotice }: Props) {
  const [playerName, setPlayerName] = useState(username);
  const [closeOnLaunch, setCloseOnLaunch] = useState(closeLauncherOnGameStart);
  const [saving, setSaving] = useState(false);
  const [buildLibrary, setBuildLibrary] = useState<NexaInGameBuildLibrary | null>(null);
  const [minecraftVersions, setMinecraftVersions] = useState<MinecraftVersionItem[]>([]);
  const [loadingBuilds, setLoadingBuilds] = useState(false);
  const [building, setBuilding] = useState(false);
  const [buildingTarget, setBuildingTarget] = useState<string | null>(null);
  const [confirmBuild, setConfirmBuild] = useState(false);
  const [selectedBuild, setSelectedBuild] = useState<NexaInGameBuildEntry | null>(null);
  const [collapsedFamilies, setCollapsedFamilies] = useState<Set<string>>(new Set());
  const [failedBuilds, setFailedBuilds] = useState<Map<string, string>>(new Map());

  const modernReleases = useMemo(
    () => minecraftVersions.filter((item) => item.stable && isSupportedReleaseRange(item.id)),
    [minecraftVersions],
  );
  const buildRows = useMemo(
    () => mergeReleaseMatrix(modernReleases, buildLibrary),
    [modernReleases, buildLibrary],
  );
  const buildFamilies = useMemo(() => groupBuildRows(buildRows), [buildRows]);

  useEffect(() => setPlayerName(username), [username]);
  useEffect(() => setCloseOnLaunch(closeLauncherOnGameStart), [closeLauncherOnGameStart]);
  useEffect(() => {
    if (!isNativeHost()) return;
    let cancelled = false;
    setLoadingBuilds(true);
    Promise.all([getNexaInGameBuildLibrary(), getMinecraftVersions()])
      .then(([library, releases]) => {
        if (cancelled) return;
        setBuildLibrary(library);
        setMinecraftVersions(releases);
      })
      .catch((error: Error) => { if (!cancelled) onNotice(error.message, "error"); })
      .finally(() => { if (!cancelled) setLoadingBuilds(false); });
    return () => { cancelled = true; };
  }, [onNotice]);

  function setBuildFailure(key: string, message?: string | null) {
    setFailedBuilds((current) => {
      const next = new Map(current);
      if (message) next.set(key, message);
      else next.delete(key);
      return next;
    });
  }

  async function save() {
    setSaving(true);
    try {
      const result = await updateSettings(playerName, closeOnLaunch);
      setPlayerName(result.username);
      setCloseOnLaunch(result.closeLauncherOnGameStart);
      onUpdated(result.username, result.closeLauncherOnGameStart);
      onNotice("Configuración guardada.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo guardar la configuración.", "error");
    } finally {
      setSaving(false);
    }
  }

  async function refreshBuilds() {
    setLoadingBuilds(true);
    try {
      const [library, releases] = await Promise.all([getNexaInGameBuildLibrary(), getMinecraftVersions()]);
      setBuildLibrary(library);
      setMinecraftVersions(releases);
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo leer la biblioteca de builds.", "error");
    } finally {
      setLoadingBuilds(false);
    }
  }

  async function generateBuilds() {
    setConfirmBuild(false);
    setBuilding(true);
    const compatible = buildRows.filter((build) => hasBuildTarget(build, buildLibrary));
    let publishedCount = 0;
    const failures: string[] = [];

    try {
      if (compatible.length === 0) throw new Error("No hay adaptadores NEXA compatibles para compilar.");

      for (const build of compatible) {
        const key = buildKey(build);
        setBuildingTarget(key);
        try {
          const result = await generateNexaInGameBuild(build.minecraftVersion, build.loader);
          if (result.published) {
            publishedCount++;
            setBuildFailure(key, null);
          } else {
            const detail = result.failures[0]?.message ?? "La build no fue publicada.";
            setBuildFailure(key, detail);
            failures.push(`${build.loader} ${build.minecraftVersion}: ${detail}`);
          }
        } catch (error) {
          const detail = error instanceof Error ? error.message : "Error de compilación";
          setBuildFailure(key, detail);
          failures.push(`${build.loader} ${build.minecraftVersion}: ${detail}`);
        }
      }

      setBuildLibrary(await getNexaInGameBuildLibrary());
      if (failures.length === 0) {
        onNotice(`${publishedCount} build(s) generadas con NEXA Compiler v2.`, "success");
      } else {
        onNotice(`${publishedCount} listas; ${failures.length} fallaron. ${failures[0]}`, "error");
      }
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudieron generar las builds NEXA In-Game.", "error");
    } finally {
      setBuildingTarget(null);
      setBuilding(false);
    }
  }

  async function generateOne(build: NexaInGameBuildEntry) {
    setSelectedBuild(null);
    const key = buildKey(build);
    setBuildingTarget(key);
    try {
      const result = await generateNexaInGameBuild(build.minecraftVersion, build.loader);
      setBuildLibrary(await getNexaInGameBuildLibrary());
      if (result.published) {
        setBuildFailure(key, null);
        onNotice(`NEXA In-Game ${build.loader} ${build.minecraftVersion} compilada con Compiler v2 y publicada localmente.`, "success");
      } else {
        const detail = result.failures[0]?.message ?? "Gradle terminó sin publicar el JAR.";
        setBuildFailure(key, detail);
        onNotice(`Falló ${build.loader} ${build.minecraftVersion}. ${detail}`, "error");
      }
    } catch (error) {
      const detail = error instanceof Error ? error.message : `No se pudo compilar ${build.loader} ${build.minecraftVersion}.`;
      setBuildFailure(key, detail);
      onNotice(detail, "error");
    } finally {
      setBuildingTarget(null);
    }
  }

  async function openBuildFolder() {
    try {
      await openNexaInGameBuildFolder();
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo abrir la carpeta de builds.", "error");
    }
  }

  function toggleFamily(id: string) {
    setCollapsedFamilies((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const anyBuildRunning = building || buildingTarget !== null;
  const canBuild = Boolean(buildLibrary?.sourceAvailable && buildLibrary.targetCount > 0) && !anyBuildRunning;
  const latestRelease = modernReleases[0]?.id ?? "—";

  return (
    <section className="page settings-page">
      <div className="hero-row settings-heading">
        <div>
          <span className="eyebrow">NEXA CLIENT</span>
          <h1>Configuración</h1>
          <p>Ajustes globales del launcher. Java sigue siendo automático por perfil.</p>
        </div>
        <button className="primary-button" type="button" disabled={saving} onClick={save}>{saving ? <Loader2 className="spin" size={16} /> : <Save size={16} />} GUARDAR</button>
      </div>

      <div className="settings-grid">
        <article className="settings-card glass-panel">
          <span className="eyebrow">JUGADOR</span>
          <h2>Perfil local</h2>
          <p>Este nombre se usa para sesiones locales/offline mientras la autenticación Microsoft permanece separada.</p>
          <label className="field-label">NOMBRE DE JUGADOR<input className="nexa-input" maxLength={16} value={playerName} onChange={(event) => setPlayerName(event.target.value)} placeholder="Player" /></label>
        </article>

        <article className="settings-card glass-panel">
          <span className="eyebrow">COMPORTAMIENTO</span>
          <h2>Al iniciar Minecraft</h2>
          <p>Controla qué ocurre con NEXA cuando el juego se inicia correctamente.</p>
          <button className={`switch-row ${closeOnLaunch ? "enabled" : ""}`} type="button" onClick={() => setCloseOnLaunch((value) => !value)}>
            <span className="switch-track"><span /></span>
            <span><strong>Cerrar launcher al iniciar</strong><small>{closeOnLaunch ? "NEXA se cerrará cuando Minecraft arranque." : "NEXA permanecerá abierto durante la sesión."}</small></span>
            {closeOnLaunch && <Check size={17} />}
          </button>
        </article>

        <article className="settings-card glass-panel settings-wide ingame-build-manager">
          <div className="build-manager-heading">
            <div>
              <span className="eyebrow">NEXA IN-GAME · COMPILER V2</span>
              <h2>Core común + adaptadores por versión</h2>
              <p>Las releases se agrupan por familia de Minecraft. Abre 1.21, 1.20 o 1.19 para ver sus versiones exactas y compilar únicamente la que necesites.</p>
            </div>
            <div className="build-manager-actions">
              <button className="ghost-button" type="button" disabled={loadingBuilds || anyBuildRunning || !isNativeHost()} onClick={refreshBuilds}><RefreshCw className={loadingBuilds ? "spin" : ""} size={15} /> ACTUALIZAR</button>
              <button className="ghost-button" type="button" disabled={anyBuildRunning || !isNativeHost()} onClick={openBuildFolder}><FolderOpen size={15} /> ABRIR CARPETA</button>
              <button className="secondary-button" type="button" disabled={!canBuild} onClick={() => setConfirmBuild(true)}>{building ? <Loader2 className="spin" size={15} /> : <Hammer size={15} />} {building ? "COMPILANDO…" : "GENERAR TODAS COMPATIBLES"}</button>
            </div>
          </div>

          <div className="build-summary-grid build-summary-grid-four">
            <BuildMetric icon={<Layers3 size={16} />} label="RELEASES 1.19+" value={modernReleases.length} />
            <BuildMetric icon={<Archive size={16} />} label="ADAPTADORES" value={buildLibrary?.targetCount ?? 0} />
            <BuildMetric icon={<Check size={16} />} label="PUBLICADAS" value={buildLibrary?.publishedCount ?? 0} />
            <BuildMetric icon={<Hammer size={16} />} label="ÚLTIMA" value={latestRelease} compact />
          </div>

          <div className="build-output-path">
            <span>Biblioteca local</span>
            <code>{buildLibrary?.outputRoot ?? (isNativeHost() ? "Leyendo ruta…" : "Disponible dentro de NEXA Desktop")}</code>
          </div>

          {!loadingBuilds && buildLibrary && !buildLibrary.sourceAvailable && (
            <div className="build-source-warning">
              <strong>Fuentes de desarrollo no detectadas.</strong>
              <span>{buildLibrary.sourceError ?? "Ejecuta NEXA desde un checkout del repositorio que contenga ingame/ para generar builds. La biblioteca ya generada sigue disponible."}</span>
            </div>
          )}

          <div className="build-library-shell">
            <div className="build-library-head">
              <span>VERSIÓN</span><span>LOADER</span><span>NEXA IN-GAME</span><span>JAR</span><span>ESTADO</span><span>ACCIÓN</span>
            </div>
            {loadingBuilds && <div className="build-library-empty"><Loader2 className="spin" size={18} /> Leyendo releases y catálogo local…</div>}
            {!loadingBuilds && buildFamilies.length === 0 && <div className="build-library-empty">No se pudieron obtener releases oficiales desde 1.19.</div>}
            {!loadingBuilds && buildFamilies.map((family) => (
              <VersionFamilyGroup
                key={family.id}
                family={family}
                collapsed={collapsedFamilies.has(family.id)}
                library={buildLibrary}
                anyBuildRunning={anyBuildRunning}
                buildingTarget={buildingTarget}
                failedBuilds={failedBuilds}
                onToggle={() => toggleFamily(family.id)}
                onBuild={setSelectedBuild}
              />
            ))}
          </div>
          {buildLibrary?.lastPublishedAt && <div className="build-library-foot">Última publicación local: {formatDate(buildLibrary.lastPublishedAt)}</div>}
        </article>

        <article className="settings-card glass-panel settings-wide about-react-card">
          <div className="about-mark"><img src="./brand/nexa-mark.png" alt="NEXA" /></div>
          <div className="about-copy">
            <img className="about-wordmark" src={nexaWordmarkDataUrl} alt="NEXA Client" />
            <span className="eyebrow">ACERCA DE NEXA</span>
            <h2>NEXA Client <small>{version}</small></h2>
            <p>Cliente de Minecraft para Windows. Backend .NET, interfaz React y perfiles físicamente aislados por GUID.</p>
            <div className="trust-row"><ShieldCheck size={17} /><span>Sin telemetría, anuncios ni acceso directo de JavaScript al sistema de archivos.</span></div>
            <div className="about-actions">
              <a className="secondary-button link-button" href="https://github.com/jr-0205" target="_blank" rel="noreferrer"><ExternalLink size={15} /> GITHUB DEL CREADOR</a>
              <a className="ghost-button link-button" href="https://chatgpt.com/download/" target="_blank" rel="noreferrer"><ExternalLink size={15} /> CHATGPT</a>
            </div>
          </div>
        </article>
      </div>

      <NexaDialog
        open={confirmBuild}
        tone="warning"
        title="Generar todas las builds compatibles con Compiler v2"
        description={`Se compilarán ${buildLibrary?.targetCount ?? 0} adaptadores uno por uno usando el mismo pipeline aislado que COMPILAR ESTA. Las releases sin adaptador no se intentarán compilar.`}
        confirmLabel="GENERAR COMPATIBLES"
        busy={building}
        onConfirm={generateBuilds}
        onCancel={() => setConfirmBuild(false)}
      />

      <NexaDialog
        open={selectedBuild !== null}
        tone="info"
        title={selectedBuild ? `Compilar ${selectedBuild.loader} ${selectedBuild.minecraftVersion}` : "Compilar build"}
        description={selectedBuild ? `Compiler v2 combinará el core común de NEXA con el adaptador de Minecraft ${selectedBuild.minecraftVersion} + ${selectedBuild.loader} en un workspace temporal. Las demás builds no se tocarán.` : ""}
        confirmLabel="COMPILAR ESTA"
        busy={buildingTarget !== null}
        onConfirm={() => selectedBuild && generateOne(selectedBuild)}
        onCancel={() => setSelectedBuild(null)}
      />
    </section>
  );
}

function BuildMetric({ icon, label, value, compact = false }: { icon: ReactNode; label: string; value: number | string; compact?: boolean }) {
  return <div className={`build-metric ${compact ? "compact" : ""}`}><span>{icon}</span><div><small>{label}</small><strong>{value}</strong></div></div>;
}

function VersionFamilyGroup({ family, collapsed, library, anyBuildRunning, buildingTarget, failedBuilds, onToggle, onBuild }: {
  family: BuildFamily;
  collapsed: boolean;
  library: NexaInGameBuildLibrary | null;
  anyBuildRunning: boolean;
  buildingTarget: string | null;
  failedBuilds: Map<string, string>;
  onToggle(): void;
  onBuild(build: NexaInGameBuildEntry): void;
}) {
  const compatibleCount = family.rows.filter((build) => hasBuildTarget(build, library)).length;
  const publishedCount = family.rows.filter((build) => build.status === "published" && build.exists).length;

  return (
    <div className={`build-family ${collapsed ? "collapsed" : ""}`}>
      <button className="build-family-header" type="button" onClick={onToggle}>
        <span className="build-family-chevron">{collapsed ? <ChevronRight size={15} /> : <ChevronDown size={15} />}</span>
        <span className="build-family-title">Minecraft {family.id}</span>
        <span className="build-family-summary">{family.rows.length} versiones · {compatibleCount} adaptadores · {publishedCount} publicadas</span>
      </button>
      {!collapsed && (
        <div className="build-family-rows">
          {family.rows.map((build) => {
            const compilable = hasBuildTarget(build, library);
            return (
              <BuildRow
                key={`${build.loader}-${build.minecraftVersion}-${build.nexaInGameVersion}`}
                build={build}
                busy={buildingTarget === buildKey(build)}
                compilable={compilable}
                failureMessage={failedBuilds.get(buildKey(build)) ?? null}
                disabled={anyBuildRunning || !library?.sourceAvailable || !compilable}
                onBuild={() => onBuild(build)}
              />
            );
          })}
        </div>
      )}
    </div>
  );
}

function BuildRow({ build, busy, disabled, compilable, failureMessage, onBuild }: { build: NexaInGameBuildEntry; busy: boolean; disabled: boolean; compilable: boolean; failureMessage: string | null; onBuild(): void }) {
  const published = build.status === "published" && build.exists;
  const planned = build.status === "planned";
  const unsupported = !compilable;
  const failed = Boolean(failureMessage) && !published;
  const state = published ? "PUBLICADA" : unsupported ? "SIN ADAPTADOR" : failed ? "FALLÓ" : planned ? "PENDIENTE" : "SIN JAR";
  const stateClass = published ? "published" : unsupported ? "unsupported" : failed ? "failed" : planned ? "planned" : "missing";
  const size = build.exists && build.sizeBytes > 0 ? ` · ${formatBytes(build.sizeBytes)}` : "";
  return (
    <div className="build-library-row build-library-child-row">
      <strong>{build.minecraftVersion}</strong>
      <span>{build.loader}</span>
      <span>{compilable ? build.nexaInGameVersion : "—"}</span>
      <span className="build-file" title={failureMessage ?? build.fileName ?? undefined}>{failed ? firstLine(failureMessage!) : compilable ? (build.fileName ?? "—") : "Adaptador todavía no portado"}{failed ? "" : size}</span>
      <span className={`build-state ${stateClass}`} title={failureMessage ?? undefined}>{state}</span>
      <button className="build-row-action" type="button" disabled={disabled} onClick={onBuild}>{busy ? <Loader2 className="spin" size={12} /> : <Hammer size={12} />}{busy ? "COMPILANDO" : unsupported ? "NO DISPONIBLE" : published ? "RECOMPILAR" : failed ? "REINTENTAR" : "COMPILAR"}</button>
    </div>
  );
}

function mergeReleaseMatrix(releases: MinecraftVersionItem[], library: NexaInGameBuildLibrary | null): NexaInGameBuildEntry[] {
  const existing = library?.builds ?? [];
  const byKey = new Map(existing.map((build) => [buildKey(build), build]));
  const rows: NexaInGameBuildEntry[] = [];

  for (const release of releases) {
    const fabricKey = `fabric::${release.id}`;
    rows.push(byKey.get(fabricKey) ?? {
      minecraftVersion: release.id,
      loader: "Fabric",
      nexaInGameVersion: "—",
      fileName: null,
      relativePath: null,
      status: "unsupported",
      exists: false,
      sizeBytes: 0,
      sha256: null,
      publishedAt: null,
    });
  }

  for (const build of existing) {
    const key = buildKey(build);
    if (!rows.some((row) => buildKey(row) === key)) rows.push(build);
  }

  return rows;
}

function groupBuildRows(rows: NexaInGameBuildEntry[]): BuildFamily[] {
  const grouped = new Map<string, NexaInGameBuildEntry[]>();
  for (const row of rows) {
    const family = versionFamily(row.minecraftVersion);
    const entries = grouped.get(family) ?? [];
    entries.push(row);
    grouped.set(family, entries);
  }

  return [...grouped.entries()]
    .map(([id, familyRows]) => ({
      id,
      rows: familyRows.sort((a, b) => compareMinecraftVersions(a.minecraftVersion, b.minecraftVersion)),
    }))
    .sort((a, b) => compareMinecraftVersions(b.id, a.id));
}

function versionFamily(version: string) {
  const parts = version.split(".").map((part) => Number.parseInt(part, 10));
  if (parts[0] === 1 && Number.isFinite(parts[1])) return `1.${parts[1]}`;
  if (Number.isFinite(parts[0])) return String(parts[0]);
  return version;
}

function compareMinecraftVersions(a: string, b: string) {
  const left = a.split(".").map((part) => Number.parseInt(part, 10));
  const right = b.split(".").map((part) => Number.parseInt(part, 10));
  const length = Math.max(left.length, right.length);
  for (let index = 0; index < length; index++) {
    const delta = (left[index] ?? 0) - (right[index] ?? 0);
    if (delta !== 0) return delta;
  }
  return a.localeCompare(b);
}

function hasBuildTarget(build: NexaInGameBuildEntry, library: NexaInGameBuildLibrary | null) {
  return Boolean(library?.targets.some((target) =>
    target.minecraftVersion === build.minecraftVersion &&
    target.loader.toLowerCase() === build.loader.toLowerCase(),
  ));
}

function isSupportedReleaseRange(id: string) {
  const parts = id.split(".").map((part) => Number.parseInt(part, 10));
  if (parts.length < 2 || parts.some((part) => Number.isNaN(part))) return false;
  if (parts[0] > 1) return true;
  return parts[0] === 1 && parts[1] >= 19;
}

function buildKey(build: Pick<NexaInGameBuildEntry, "loader" | "minecraftVersion">) {
  return `${build.loader.toLowerCase()}::${build.minecraftVersion}`;
}

function firstLine(value: string) {
  return value.split(/\r?\n/, 1)[0] ?? value;
}

function formatBytes(value: number) {
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
