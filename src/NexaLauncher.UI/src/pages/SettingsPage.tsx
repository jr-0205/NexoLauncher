import { type ReactNode, useEffect, useState } from "react";
import { Archive, Check, ExternalLink, FolderOpen, Hammer, Loader2, RefreshCw, Save, ShieldCheck } from "lucide-react";
import {
  generateNexaInGameBuild,
  generateNexaInGameBuilds,
  getNexaInGameBuildLibrary,
  isNativeHost,
  openNexaInGameBuildFolder,
  updateSettings,
} from "../app/nexa-bridge";
import type { NexaInGameBuildEntry, NexaInGameBuildLibrary } from "../app/types";
import { nexaWordmarkDataUrl } from "../brand/nexa-wordmark";
import { NexaDialog } from "../components/NexaDialog";

type Props = {
  username: string;
  closeLauncherOnGameStart: boolean;
  version: string;
  onUpdated(username: string, closeLauncherOnGameStart: boolean): void;
  onNotice(message: string, kind?: "success" | "error"): void;
};

export function SettingsPage({ username, closeLauncherOnGameStart, version, onUpdated, onNotice }: Props) {
  const [playerName, setPlayerName] = useState(username);
  const [closeOnLaunch, setCloseOnLaunch] = useState(closeLauncherOnGameStart);
  const [saving, setSaving] = useState(false);
  const [buildLibrary, setBuildLibrary] = useState<NexaInGameBuildLibrary | null>(null);
  const [loadingBuilds, setLoadingBuilds] = useState(false);
  const [building, setBuilding] = useState(false);
  const [buildingTarget, setBuildingTarget] = useState<string | null>(null);
  const [confirmBuild, setConfirmBuild] = useState(false);
  const [selectedBuild, setSelectedBuild] = useState<NexaInGameBuildEntry | null>(null);

  useEffect(() => setPlayerName(username), [username]);
  useEffect(() => setCloseOnLaunch(closeLauncherOnGameStart), [closeLauncherOnGameStart]);
  useEffect(() => {
    if (!isNativeHost()) return;
    let cancelled = false;
    setLoadingBuilds(true);
    getNexaInGameBuildLibrary()
      .then((value) => { if (!cancelled) setBuildLibrary(value); })
      .catch((error: Error) => { if (!cancelled) onNotice(error.message, "error"); })
      .finally(() => { if (!cancelled) setLoadingBuilds(false); });
    return () => { cancelled = true; };
  }, [onNotice]);

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
      setBuildLibrary(await getNexaInGameBuildLibrary());
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo leer la biblioteca de builds.", "error");
    } finally {
      setLoadingBuilds(false);
    }
  }

  async function generateBuilds() {
    setConfirmBuild(false);
    setBuilding(true);
    try {
      const result = await generateNexaInGameBuilds();
      setBuildLibrary(result.library);
      if (result.failureCount === 0) {
        onNotice(`${result.publishedCount} build(s) NEXA In-Game generadas y verificadas.`, "success");
      } else {
        const first = result.failures[0];
        onNotice(`${result.publishedCount} listas; ${result.failureCount} fallaron.${first ? ` ${first.loader} ${first.minecraftVersion}: ${first.message}` : ""}`, "error");
      }
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudieron generar las builds NEXA In-Game.", "error");
    } finally {
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
        onNotice(`NEXA In-Game ${build.loader} ${build.minecraftVersion} compilada y publicada localmente.`, "success");
      } else {
        const detail = result.failures[0]?.message;
        onNotice(`Falló ${build.loader} ${build.minecraftVersion}.${detail ? ` ${detail}` : ""}`, "error");
      }
    } catch (error) {
      onNotice(error instanceof Error ? error.message : `No se pudo compilar ${build.loader} ${build.minecraftVersion}.`, "error");
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

  const anyBuildRunning = building || buildingTarget !== null;
  const canBuild = Boolean(buildLibrary?.sourceAvailable && buildLibrary.targetCount > 0) && !anyBuildRunning;

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
              <span className="eyebrow">NEXA IN-GAME · BUILDS</span>
              <h2>Generador y biblioteca de builds</h2>
              <p>Compila sólo la versión que necesites desde su fila. “Generar todas” queda disponible para regenerar la matriz completa cuando realmente haga falta.</p>
            </div>
            <div className="build-manager-actions">
              <button className="ghost-button" type="button" disabled={loadingBuilds || anyBuildRunning || !isNativeHost()} onClick={refreshBuilds}><RefreshCw className={loadingBuilds ? "spin" : ""} size={15} /> ACTUALIZAR</button>
              <button className="ghost-button" type="button" disabled={anyBuildRunning || !isNativeHost()} onClick={openBuildFolder}><FolderOpen size={15} /> ABRIR CARPETA</button>
              <button className="secondary-button" type="button" disabled={!canBuild} onClick={() => setConfirmBuild(true)}>{building ? <Loader2 className="spin" size={15} /> : <Hammer size={15} />} {building ? "COMPILANDO…" : "GENERAR TODAS"}</button>
            </div>
          </div>

          <div className="build-summary-grid">
            <BuildMetric icon={<Archive size={16} />} label="OBJETIVOS" value={buildLibrary?.targetCount ?? 0} />
            <BuildMetric icon={<Check size={16} />} label="PUBLICADAS" value={buildLibrary?.publishedCount ?? 0} />
            <BuildMetric icon={<Hammer size={16} />} label="PENDIENTES" value={buildLibrary?.pendingCount ?? 0} />
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
            {loadingBuilds && <div className="build-library-empty"><Loader2 className="spin" size={18} /> Leyendo catálogo local…</div>}
            {!loadingBuilds && (buildLibrary?.builds.length ?? 0) === 0 && <div className="build-library-empty">Todavía no hay objetivos ni builds registradas.</div>}
            {!loadingBuilds && buildLibrary?.builds.map((build) => (
              <BuildRow
                key={`${build.loader}-${build.minecraftVersion}-${build.nexaInGameVersion}`}
                build={build}
                busy={buildingTarget === buildKey(build)}
                disabled={anyBuildRunning || !buildLibrary.sourceAvailable}
                onBuild={() => setSelectedBuild(build)}
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
        title="Generar todas las builds NEXA In-Game"
        description={`Se compilarán ${buildLibrary?.targetCount ?? 0} objetivo(s) desde ingame/. Usa esta opción sólo cuando necesites regenerar toda la matriz.`}
        confirmLabel="GENERAR TODAS"
        busy={building}
        onConfirm={generateBuilds}
        onCancel={() => setConfirmBuild(false)}
      />

      <NexaDialog
        open={selectedBuild !== null}
        tone="info"
        title={selectedBuild ? `Compilar ${selectedBuild.loader} ${selectedBuild.minecraftVersion}` : "Compilar build"}
        description={selectedBuild ? `Sólo se compilará NEXA In-Game ${selectedBuild.nexaInGameVersion} para Minecraft ${selectedBuild.minecraftVersion} + ${selectedBuild.loader}. Las demás builds de la biblioteca no se tocarán.` : ""}
        confirmLabel="COMPILAR ESTA"
        busy={buildingTarget !== null}
        onConfirm={() => selectedBuild && generateOne(selectedBuild)}
        onCancel={() => setSelectedBuild(null)}
      />
    </section>
  );
}

function BuildMetric({ icon, label, value }: { icon: ReactNode; label: string; value: number }) {
  return <div className="build-metric"><span>{icon}</span><div><small>{label}</small><strong>{value}</strong></div></div>;
}

function BuildRow({ build, busy, disabled, onBuild }: { build: NexaInGameBuildEntry; busy: boolean; disabled: boolean; onBuild(): void }) {
  const published = build.status === "published" && build.exists;
  const planned = build.status === "planned";
  const state = published ? "PUBLICADA" : planned ? "PENDIENTE" : "SIN JAR";
  const size = build.exists && build.sizeBytes > 0 ? ` · ${formatBytes(build.sizeBytes)}` : "";
  return (
    <div className="build-library-row">
      <strong>{build.minecraftVersion}</strong>
      <span>{build.loader}</span>
      <span>{build.nexaInGameVersion}</span>
      <span className="build-file" title={build.fileName ?? undefined}>{build.fileName ?? "—"}{size}</span>
      <span className={`build-state ${published ? "published" : planned ? "planned" : "missing"}`}>{state}</span>
      <button className="build-row-action" type="button" disabled={disabled} onClick={onBuild}>{busy ? <Loader2 className="spin" size={12} /> : <Hammer size={12} />}{busy ? "COMPILANDO" : published ? "RECOMPILAR" : "COMPILAR"}</button>
    </div>
  );
}

function buildKey(build: NexaInGameBuildEntry) {
  return `${build.loader.toLowerCase()}::${build.minecraftVersion}`;
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
