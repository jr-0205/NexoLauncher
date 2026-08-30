import { Clipboard, RefreshCw, Terminal } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { getProfileLiveLogs, onBridgeEvent } from "../app/nexa-bridge";
import type { NexaProfile, ProfileLiveLogs, ProfileLogSnapshot } from "../app/types";

type Props = {
  profile: NexaProfile;
  running: boolean;
  onNotice(message: string, kind?: "success" | "error"): void;
};

type LogTab = "game" | "launcher" | "crash";

const EMPTY_LOG: ProfileLogSnapshot = {
  available: false,
  path: null,
  text: "",
  updatedAt: null,
  sizeBytes: 0,
};

function tailLines(value: string, maximum = 600) {
  const lines = value.replace(/\r/g, "").split("\n");
  return lines.length <= maximum ? lines.join("\n") : lines.slice(-maximum).join("\n");
}

function lineTone(line: string) {
  if (/\b(fatal|error|exception|crash|caused by:)\b/i.test(line)) return "error";
  if (/\b(warn|warning)\b/i.test(line)) return "warning";
  return "normal";
}

export function ProfileLiveConsole({ profile, running, onNotice }: Props) {
  const [logTab, setLogTab] = useState<LogTab>("game");
  const [logs, setLogs] = useState<ProfileLiveLogs | null>(null);
  const [logError, setLogError] = useState<string | null>(null);
  const [exitCode, setExitCode] = useState<number | null>(null);
  const [following, setFollowing] = useState(true);
  const consoleBody = useRef<HTMLDivElement>(null);

  async function refreshLogs() {
    try {
      const result = await getProfileLiveLogs(profile.id);
      setLogs(result);
      setLogError(null);
      return result;
    } catch (error) {
      setLogError(error instanceof Error ? error.message : "No se pudieron leer los logs del perfil.");
      return null;
    }
  }

  useEffect(() => {
    setLogs(null);
    setLogError(null);
    setExitCode(null);
    setLogTab("game");
    void refreshLogs();

    const timer = window.setInterval(() => void refreshLogs(), running ? 500 : 1500);
    return () => window.clearInterval(timer);
  }, [profile.id, running]);

  useEffect(() => {
    const offStarted = onBridgeEvent<{ profileId: string }>("launch.started", ({ profileId }) => {
      if (profileId !== profile.id) return;
      setExitCode(null);
      setFollowing(true);
      setLogTab("game");
      window.setTimeout(() => void refreshLogs(), 100);
    });

    const offExited = onBridgeEvent<{ profileId: string; exitCode: number }>("launch.exited", ({ profileId, exitCode: code }) => {
      if (profileId !== profile.id) return;
      setExitCode(code);
      window.setTimeout(async () => {
        const result = await refreshLogs();
        if (code !== 0 && result?.crash.available && result.crash.text.trim()) setLogTab("crash");
      }, 300);
      window.setTimeout(() => void refreshLogs(), 1200);
    });

    return () => {
      offStarted();
      offExited();
    };
  }, [profile.id]);

  const activeSnapshot = logs?.[logTab] ?? EMPTY_LOG;
  const activeText = useMemo(() => tailLines(activeSnapshot.text), [activeSnapshot.text]);

  useEffect(() => {
    if (!following || !consoleBody.current) return;
    consoleBody.current.scrollTop = consoleBody.current.scrollHeight;
  }, [activeText, following, logTab]);

  async function copyLog() {
    if (!activeText.trim()) return;
    try {
      await navigator.clipboard.writeText(activeText);
      onNotice("Log copiado al portapapeles.", "success");
    } catch {
      onNotice("No se pudo copiar el log.", "error");
    }
  }

  function handleScroll() {
    const element = consoleBody.current;
    if (!element) return;
    const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
    setFollowing(distanceFromBottom < 36);
  }

  const sessionLabel = running
    ? "EN EJECUCIÓN"
    : exitCode === null
      ? "EN ESPERA"
      : exitCode === 0
        ? "CERRADO · 0"
        : `ERROR · CÓDIGO ${exitCode}`;

  return (
    <section className="profile-live-console profile-live-console-inline" aria-label={`Logs en vivo de ${profile.name}`}>
      <header className="live-console-heading">
        <div className="live-console-title">
          <Terminal size={18} />
          <div>
            <span className="eyebrow">DIAGNÓSTICO DEL PERFIL</span>
            <strong>Logs en tiempo real</strong>
            <span>Minecraft {profile.minecraftVersion} · {profile.loader}{profile.loaderVersion ? ` ${profile.loaderVersion}` : ""}</span>
          </div>
        </div>
        <div className="live-console-actions">
          <span className={`live-console-status ${running ? "live" : exitCode !== null && exitCode !== 0 ? "failed" : ""}`}>{sessionLabel}</span>
          {!following && <button type="button" onClick={() => setFollowing(true)}>SEGUIR FINAL</button>}
          <button type="button" onClick={() => void refreshLogs()}><RefreshCw size={13} /> ACTUALIZAR</button>
          <button type="button" disabled={!activeText.trim()} onClick={() => void copyLog()}><Clipboard size={13} /> COPIAR</button>
        </div>
      </header>

      <div className="live-console-tabs">
        <button className={logTab === "game" ? "active" : ""} type="button" onClick={() => { setLogTab("game"); setFollowing(true); }}>MINECRAFT · latest.log</button>
        <button className={logTab === "launcher" ? "active" : ""} type="button" onClick={() => { setLogTab("launcher"); setFollowing(true); }}>NEXA · STDOUT/STDERR</button>
        <button className={`${logTab === "crash" ? "active" : ""} ${logs?.crash.available ? "has-crash" : ""}`} type="button" onClick={() => { setLogTab("crash"); setFollowing(true); }}>CRASH REPORT{logs?.crash.available ? " · DETECTADO" : ""}</button>
      </div>

      {logError ? (
        <div className="live-console-body empty">{logError}</div>
      ) : activeText.trim() ? (
        <div className="live-console-body" ref={consoleBody} onScroll={handleScroll}>
          <pre>{activeText.split("\n").map((line, index) => <span className={`log-line ${lineTone(line)}`} key={`${index}-${line.slice(0, 18)}`}>{line}{"\n"}</span>)}</pre>
        </div>
      ) : (
        <div className="live-console-body empty" ref={consoleBody}>Todavía no hay datos en esta fuente. Inicia Minecraft para capturar la sesión.</div>
      )}

      <div className="live-console-path" title={activeSnapshot.path ?? ""}>{activeSnapshot.path ?? "Sin archivo todavía"}</div>
    </section>
  );
}
