import { useCallback, useEffect, useMemo, useState } from "react";
import { Loader2, X } from "lucide-react";
import { bootstrap, launchProfile, onBridgeEvent } from "./nexa-bridge";
import type { BootstrapData, NexaProfile, OperationProgress } from "./types";
import { Sidebar } from "../components/Sidebar";
import { Topbar } from "../components/Topbar";
import { LibraryPage } from "../pages/LibraryPage";
import { CreateProfilePage } from "../pages/CreateProfilePage";
import { ProfileDetailPage } from "../pages/ProfileDetailPage";
import { ContentPage } from "../pages/ContentPage";
import { SettingsPage } from "../pages/SettingsPage";

type Section = "library" | "create" | "profile" | "content" | "settings";
type SidebarSection = "library" | "create" | "content" | "settings";
type Notice = { id: number; message: string; kind: "success" | "error" };

const titleBySection: Record<Section, string> = {
  library: "Biblioteca",
  create: "Crear perfil",
  profile: "Perfil",
  content: "Contenido",
  settings: "Configuración",
};

export default function App() {
  const [section, setSection] = useState<Section>("library");
  const [data, setData] = useState<BootstrapData | null>(null);
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null);
  const [launchingProfileId, setLaunchingProfileId] = useState<string | null>(null);
  const [operation, setOperation] = useState<OperationProgress | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [fatalError, setFatalError] = useState<string | null>(null);

  const showNotice = useCallback((message: string, kind: "success" | "error" = "success") => {
    setNotice({ id: Date.now(), message, kind });
  }, []);

  const refresh = useCallback(async () => {
    const next = await bootstrap();
    setData(next);
    setFatalError(null);
    return next;
  }, []);

  useEffect(() => {
    refresh().catch((reason: Error) => setFatalError(reason.message));
  }, [refresh]);

  useEffect(() => {
    const offProgress = onBridgeEvent<OperationProgress>("operation.progress", (value) => {
      setOperation(value);
      if ((value.total ?? 0) > 0 && value.completed === value.total) window.setTimeout(() => setOperation(null), 1100);
    });
    const offStarted = onBridgeEvent<{ profileId: string }>("launch.started", ({ profileId }) => {
      setLaunchingProfileId(null);
      setOperation(null);
      setData((current) => current ? { ...current, activeLaunch: { profileId, pid: 0, logPath: "" } } : current);
      showNotice("Minecraft se inició correctamente.");
    });
    const offExited = onBridgeEvent<{ profileId: string; exitCode: number; error?: string }>("launch.exited", ({ exitCode, error }) => {
      setData((current) => current ? { ...current, activeLaunch: null } : current);
      setLaunchingProfileId(null);
      if (error) showNotice(error, "error");
      else if (exitCode !== 0) showNotice(`Minecraft terminó con código ${exitCode}.`, "error");
    });
    return () => { offProgress(); offStarted(); offExited(); };
  }, [showNotice]);

  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(null), notice.kind === "error" ? 6500 : 3500);
    return () => window.clearTimeout(timer);
  }, [notice]);

  const profiles = data?.profiles ?? [];
  const selectedProfile = useMemo(
    () => profiles.find((profile) => profile.id === selectedProfileId) ?? null,
    [profiles, selectedProfileId],
  );

  const navigate = useCallback((target: SidebarSection) => {
    setSection(target);
    if (target === "library" || target === "create" || target === "settings") setSelectedProfileId(null);
  }, []);

  const openProfile = useCallback((profile: NexaProfile) => {
    setSelectedProfileId(profile.id);
    setSection("profile");
  }, []);

  const openContent = useCallback((profile: NexaProfile) => {
    setSelectedProfileId(profile.id);
    setSection("content");
  }, []);

  const play = useCallback(async (profile: NexaProfile) => {
    if (launchingProfileId) return;
    setLaunchingProfileId(profile.id);
    try {
      const result = await launchProfile(profile.id);
      setData((current) => current ? {
        ...current,
        profiles: current.profiles.map((item) => item.id === result.profile.id ? result.profile : item),
        activeLaunch: { profileId: profile.id, pid: result.pid, logPath: result.logPath },
      } : current);
    } catch (error) {
      setLaunchingProfileId(null);
      setOperation(null);
      showNotice(error instanceof Error ? error.message : "No se pudo iniciar Minecraft.", "error");
    }
  }, [launchingProfileId, showNotice]);

  const replaceProfile = useCallback((profile: NexaProfile) => {
    setData((current) => current ? {
      ...current,
      profiles: current.profiles.map((item) => item.id === profile.id ? profile : item),
    } : current);
    setSelectedProfileId(profile.id);
  }, []);

  const activeSidebar: SidebarSection = section === "profile" ? "library" : section;
  const title = section === "profile" && selectedProfile ? selectedProfile.name : titleBySection[section];

  return (
    <div className="app-shell">
      <div className="ambient ambient-one" />
      <div className="ambient ambient-two" />
      <Sidebar active={activeSidebar} onChange={navigate} />
      <div className="workspace">
        <Topbar title={title} username={data?.username ?? "Player"} />
        <main className="content-scroll">
          {fatalError && <div className="inline-error"><strong>NEXA no pudo cargar el launcher.</strong><span>{fatalError}</span><button type="button" onClick={() => refresh().catch((reason: Error) => setFatalError(reason.message))}>REINTENTAR</button></div>}

          {section === "library" && <LibraryPage profiles={profiles} launchingProfileId={launchingProfileId} onCreate={() => navigate("create")} onOpen={openProfile} onPlay={play} />}
          {section === "create" && <CreateProfilePage onCancel={() => navigate("library")} onNotice={showNotice} onCreated={(profile) => { setData((current) => current ? { ...current, profiles: [profile, ...current.profiles.filter((item) => item.id !== profile.id)] } : current); openProfile(profile); }} />}
          {section === "profile" && selectedProfile && <ProfileDetailPage key={selectedProfile.id} profile={selectedProfile} launching={launchingProfileId === selectedProfile.id} onLaunch={play} onContent={openContent} onUpdated={replaceProfile} onDeleted={() => { setData((current) => current ? { ...current, profiles: current.profiles.filter((item) => item.id !== selectedProfile.id) } : current); navigate("library"); }} onBack={() => navigate("library")} onNotice={showNotice} />}
          {section === "profile" && !selectedProfile && <div className="page"><div className="empty-state glass-panel"><h2>Perfil no disponible</h2><p>Vuelve a Biblioteca y selecciona un perfil.</p></div></div>}
          {section === "content" && <ContentPage profiles={profiles} initialProfileId={selectedProfileId} onSelectProfile={setSelectedProfileId} onNotice={showNotice} />}
          {section === "settings" && <SettingsPage username={data?.username ?? "Player"} closeLauncherOnGameStart={data?.closeLauncherOnGameStart ?? true} version={data?.version ?? "0.5.2"} onUpdated={(username, closeLauncherOnGameStart) => setData((current) => current ? { ...current, username, closeLauncherOnGameStart } : current)} onNotice={showNotice} />}
        </main>
      </div>

      {operation && (
        <div className="operation-pill glass-panel">
          <Loader2 className="spin" size={16} />
          <div><strong>{operation.stage}</strong>{(operation.total ?? 0) > 0 && <span>{operation.completed ?? 0} / {operation.total}</span>}</div>
          {(operation.total ?? 0) > 0 && <div className="operation-track"><span style={{ width: `${Math.max(0, Math.min(100, operation.percentage ?? (((operation.completed ?? 0) / Math.max(1, operation.total ?? 1)) * 100)))}%` }} /></div>}
        </div>
      )}

      {notice && <div key={notice.id} className={`nexa-toast ${notice.kind}`}><span>{notice.message}</span><button className="icon-button" type="button" onClick={() => setNotice(null)}><X size={15} /></button></div>}
    </div>
  );
}
