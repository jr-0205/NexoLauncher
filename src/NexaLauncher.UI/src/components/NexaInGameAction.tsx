import { Check, Clipboard, Gamepad2, Loader2, RefreshCw, Terminal, X } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { getNexaInGameStatus, getProfileLiveLogs, installNexaInGame, onBridgeEvent } from "../app/nexa-bridge";
import type { NexaInGameStatus, NexaProfile, ProfileLiveLogs, ProfileLogSnapshot } from "../app/types";

type Props = {
  profile: NexaProfile;
  launching: boolean;
  onNotice(message: string, kind?: "success" | "error"): void;
};

type LogTab = "game" | "launcher" | "crash";

const EMPTY_LOG: ProfileLogSnapshot = { available: false, path: null, text: "", updatedAt: null, sizeBytes: 0 };

function tailLines(value: string, maximum = 600) {
  const lines = value.replace(/\r/g, "").split("\n");
  return lines.length <= maximum ? lines.join("\n") : lines.slice(-maximum).join("\n");
}

function lineTone(line: string) {
  if (/\b(fatal|error|exception|crash|caused by:)\b/i.test(line)) return "error";
  if (/\b(warn|warning)\b/i.test(line)) return "warning";
  return "normal";
}

export function NexaInGameAction({ profile, launching, onNotice }: Props) {
  const [status, setStatus] = useState<NexaInGameStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [consoleOpen, setConsoleOpen] = useState(false);
  const [logTab, setLogTab] = useState<LogTab>("game");
  const [logs, setLogs] = useState<ProfileLiveLogs | null>(null);
  const [logError, setLogError] = useState<string | null>(null);
  const [exitCode, setExitCode] = useState<number | null>(null);
  const consoleBody = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let active = true;
    setStatus(null);
    getNexaInGameStatus(profile.id)
      .then((result) => { if (active) setStatus(result); })
      .catch((error: Error) => {
        if (!active) return;
        onNotice(error.message, "error");
      });
    return () => { active = false; };
  }, [profile.id]);

  async function refreshLogs() {
    try {
      const result = await getProfileLiveLogs(profile.id);
      setLogs(result);
      setLogError(null);
    } catch (error) {
      setLogError(error instanceof Error ? error.message : "No se pudieron leer los logs del perfil.");
    }
  }

  useEffect(() => {
    if (!consoleOpen) return;
    void refreshLogs();
    const timer = window.setInterval(() => void refreshLogs(), 750);
    return () => window.clearInterval(timer);
  }, [consoleOpen, profile.id]);

  useEffect(() => {
    const offStarted = onBridgeEvent<{ profileId: string }>("launch.started", ({ profileId }) => {
      if (profileId !== profile.id) return;
      setExitCode(null);
      setConsoleOpen(true);
      window.setTimeout(() => void refreshLogs(), 100);
    });
    const offExited = onBridgeEvent<{ profileId: string; exitCode: number }>("launch.exited", ({ profileId, exitCode: code }) => {
      if (profileId !== profile.id) return;
      setExitCode(code);
      window.setTimeout(() => void refreshLogs(), 250);
      window.setTimeout(() => void refreshLogs(), 1000);
    });
    return () => { offStarted(); offExited(); };
  }, [profile.id]);

  const activeSnapshot = logs?.[logTab] ?? EMPTY_LOG;
  const activeText = useMemo(() => tailLines(activeSnapshot.text), [activeSnapshot.text]);

  useEffect(() => {
    if (!consoleOpen || !consoleBody.current) return;
    consoleBody.current.scrollTop = consoleBody.current.scrollHeight;
  }, [activeText, consoleOpen, logTab]);

  async function copyLog() {
    if (!activeText.trim()) return;
    try {
      await navigator.clipboard.writeText(activeText);
      onNotice("Log copiado al portapapeles.", "success");
    } catch {
      onNotice("No se pudo copiar el log.", "error");
    }
  }

  async function installOrRepair() {
    if (busy || launching || !status) return;

    if (!status.available) {
      onNotice(status.installed
        ? "NEXA In-Game está instalado, pero no hay una build publicada disponible para reinstalar o actualizar ahora mismo."
        : status.message, "error");
      return;
    }

    const wasInstalled = status.installed;
    setBusy(true);
    try {
      const result = await installNexaInGame(profile.id);
      const refreshed = await getNexaInGameStatus(profile.id);
      setStatus(refreshed);
      const cache = result.usedCache ? " Se reutilizó la caché verificada." : "";
      onNotice(
        wasInstalled
          ? `NEXA In-Game ${result.version} reinstalado/actualizado correctamente.${cache}`
          : `NEXA In-Game ${result.version} instalado. Shift derecho ya está listo.${cache}`,
        "success",
      );
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo instalar NEXA In-Game.", "error");
    } finally {
      setBusy(false);
    }
  }

  const label = busy
    ? status?.installed ? "REINSTALANDO / ACTUALIZANDO…" : "INSTALANDO NEXA IN-GAME…"
    : status?.installed
      ? "NEXA IN-GAME · LISTO"
      : status?.available
        ? "AÑADIR NEXA IN-GAME"
        : status
          ? "NEXA IN-GAME · BUILD PENDIENTE"
          : "NEXA IN-GAME";

  return (
    <div className="ingame-action-stack">
      <button
        className={`secondary-button ${status?.installed ? "boost-active-button" : ""}`}
        type="button"
        disabled={busy || launching || !status || (!status.installed && !status.available)}
        title={status?.message ?? "Comprobando una build compatible con este perfil…"}
        onClick={status?.installed ? undefined : installOrRepair}
      >
        {busy ? <Loader2 className="spin" size={16} /> : status?.installed ? <Check size={16} /> : <Gamepad2 size={16} />}
        {label}
      </button>

      {status?.installed && (
        <button
          className="ghost-button ingame-maintenance-button"
          type="button"
          disabled={busy || launching || !status.available}
          title={status.available
            ? "Instala la build publicada más reciente compatible. Si es la misma, la reinstala y vuelve a verificarla."
            : "No hay una build publicada disponible para mantenimiento en este momento."}
          onClick={installOrRepair}
        >
          {busy ? <Loader2 className="spin" size={14} /> : <RefreshCw size={14} />}
          REINSTALAR / ACTUALIZAR
        </button>
      )}

      <button className="ghost-button ingame-maintenance-button" type="button" onClick={() => setConsoleOpen(true)}>
        <Terminal size={14} /> CONSOLA EN VIVO
      </button>

      {consoleOpen && (
        <div className="profile-live-console-overlay" role="dialog" aria-modal="true" aria-label={`Consola en vivo de ${profile.name}`}>
          <section className="profile-live-console glass-panel">
            <header className="live-console-heading">
              <div className="live-console-title">
                <Terminal size={18} />
                <div>
                  <strong>CONSOLA EN VIVO · {profile.name}</strong>
                  <span>Minecraft {profile.minecraftVersion} · {profile.loader}{profile.loaderVersion ? ` ${profile.loaderVersion}` : ""}</span>
                </div>
              </div>
              <div className="live-console-actions">
                <span className={`live-console-status ${launching ? "live" : ""}`}>{launching ? "EN EJECUCIÓN" : exitCode === null ? "ÚLTIMA SESIÓN" : `CERRADO · ${exitCode}`}</span>
                <button type="button" onClick={() => void refreshLogs()}><RefreshCw size={13} /> ACTUALIZAR</button>
                <button type="button" disabled={!activeText.trim()} onClick={() => void copyLog()}><Clipboard size={13} /> COPIAR</button>
                <button type="button" onClick={() => setConsoleOpen(false)}><X size={13} /> CERRAR</button>
              </div>
            </header>

            <div className="live-console-tabs">
              <button className={logTab === "game" ? "active" : ""} type="button" onClick={() => setLogTab("game")}>MINECRAFT · latest.log</button>
              <button className={logTab === "launcher" ? "active" : ""} type="button" onClick={() => setLogTab("launcher")}>NEXA · STDOUT/STDERR</button>
              <button className={`${logTab === "crash" ? "active" : ""} ${logs?.crash.available ? "has-crash" : ""}`} type="button" onClick={() => setLogTab("crash")}>CRASH REPORT{logs?.crash.available ? " · DETECTADO" : ""}</button>
            </div>

            {logError ? (
              <div className="live-console-body empty">{logError}</div>
            ) : activeText.trim() ? (
              <div className="live-console-body" ref={consoleBody}>
                <pre>{activeText.split("\n").map((line, index) => <span className={`log-line ${lineTone(line)}`} key={`${index}-${line.slice(0, 18)}`}>{line}{"\n"}</span>)}</pre>
              </div>
            ) : (
              <div className="live-console-body empty" ref={consoleBody}>Todavía no hay datos en esta fuente. Inicia Minecraft para capturar la sesión.</div>
            )}
            <div className="live-console-path" title={activeSnapshot.path ?? ""}>{activeSnapshot.path ?? "Sin archivo todavía"}</div>
          </section>
        </div>
      )}
    </div>
  );
}
