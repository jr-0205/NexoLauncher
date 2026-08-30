import { useCallback, useEffect, useMemo, useState } from "react";
import { Loader2, X } from "lucide-react";
import {
  bootstrap,
  getAccountStatus,
  launchProfile,
  listArtworkPlacements,
  onBridgeEvent,
  signInMicrosoft,
  signOutMicrosoft,
  updateSettings,
  uploadMicrosoftSkin,
} from "./nexa-bridge";
import type { BootstrapData, NexaAccountState, NexaProfile, OperationProgress } from "./types";
import { defaultArtworkPlacement } from "./types";
import { Sidebar } from "../components/Sidebar";
import { Topbar } from "../components/Topbar";
import { LibraryPage } from "../pages/LibraryPage";
import { CreateProfilePage } from "../pages/CreateProfilePage";
import { ProfileDetailPage } from "../pages/ProfileDetailPage";
import { ContentPage } from "../pages/ContentPage";
import { AccountPage, type SkinVariant } from "../pages/AccountPage";
import { SettingsPage } from "../pages/SettingsPage";

type Section = "library" | "create" | "profile" | "content" | "account" | "settings";
type SidebarSection = "library" | "create" | "content" | "account" | "settings";
type Notice = { id: number; message: string; kind: "success" | "error" };

const emptyAccount: NexaAccountState = {
  configured: false,
  signedIn: false,
  premium: false,
  minecraftId: null,
  minecraftName: null,
  microsoftAccount: null,
  skins: [],
  capes: [],
  activeSkinUrl: null,
  activeSkinVariant: null,
  message: null,
};

const titleBySection: Record<Section, string> = {
  library: "Biblioteca",
  create: "Crear perfil",
  profile: "Perfil",
  content: "Contenido",
  account: "Cuenta",
  settings: "Configuración",
};

export default function App() {
  const [section, setSection] = useState<Section>("library");
  const [data, setData] = useState<BootstrapData | null>(null);
  const [account, setAccount] = useState<NexaAccountState>(emptyAccount);
  const [accountBusy, setAccountBusy] = useState(false);
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null);
  const [launchingProfileId, setLaunchingProfileId] = useState<string | null>(null);
  const [operation, setOperation] = useState<OperationProgress | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [fatalError, setFatalError] = useState<string | null>(null);

  const showNotice = useCallback((message: string, kind: "success" | "error" = "success") => {
    setNotice({ id: Date.now(), message, kind });
  }, []);

  const refreshAccount = useCallback(async () => {
    const next = await getAccountStatus();
    setAccount(next);
    return next;
  }, []);

  const refresh = useCallback(async () => {
    const next = await bootstrap();
    try {
      const placements = await listArtworkPlacements();
      const map = new Map(placements.map((entry) => [entry.id, entry.artwork]));
      next.profiles = next.profiles.map((profile) => ({
        ...profile,
        artwork: map.get(profile.id) ?? profile.artwork ?? defaultArtworkPlacement,
      }));
    } catch {
      next.profiles = next.profiles.map((profile) => ({ ...profile, artwork: profile.artwork ?? defaultArtworkPlacement }));
    }
    setData(next);
    setFatalError(null);
    return next;
  }, []);

  useEffect(() => {
    Promise.all([refresh(), refreshAccount()]).catch((reason: Error) => setFatalError(reason.message));
  }, [refresh, refreshAccount]);

  useEffect(() => {
    const offProgress = onBridgeEvent<OperationProgress>("operation.progress", (value) => {
      setOperation(value);
      if ((value.total ?? 0) > 0 && value.completed === value.total) window.setTimeout(() => setOperation(null), 1100);
    });
    const offStarted = onBridgeEvent<{ profileId: string }>("launch.started", ({ profileId }) => {
      setLaunchingProfileId(null);
      setOperation(null);
      setData((current) => current ? { ...current, activeLaunch: { profileId, pid: 0, logPath: "" } } : current);
      showNotice(account.premium ? "Minecraft se inició con tu cuenta premium." : "Minecraft se inició correctamente.");
    });
    const offExited = onBridgeEvent<{ profileId: string; exitCode: number; error?: string }>("launch.exited", ({ exitCode, error }) => {
      setData((current) => current ? { ...current, activeLaunch: null } : current);
      setLaunchingProfileId(null);
      if (error) showNotice(error, "error");
      else if (exitCode !== 0) showNotice(`Minecraft terminó con código ${exitCode}.`, "error");
    });
    return () => { offProgress(); offStarted(); offExited(); };
  }, [account.premium, showNotice]);

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
  const selectedProfileBusy = selectedProfile
    ? launchingProfileId === selectedProfile.id || data?.activeLaunch?.profileId === selectedProfile.id
    : false;

  const navigate = useCallback((target: SidebarSection) => {
    setSection(target);
    if (target === "library" || target === "create" || target === "account" || target === "settings") setSelectedProfileId(null);
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
        profiles: current.profiles.map((item) => item.id === result.profile.id
          ? { ...result.profile, artwork: result.profile.artwork ?? item.artwork ?? defaultArtworkPlacement }
          : item),
        activeLaunch: { profileId: profile.id, pid: result.pid, logPath: result.logPath },
      } : current);
      if (data?.closeLauncherOnGameStart) window.close();
    } catch (error) {
      setLaunchingProfileId(null);
      setOperation(null);
      showNotice(error instanceof Error ? error.message : "No se pudo iniciar Minecraft.", "error");
    }
  }, [data?.closeLauncherOnGameStart, launchingProfileId, showNotice]);

  const updateLocalUsername = useCallback(async (username: string) => {
    const result = await updateSettings(username, data?.closeLauncherOnGameStart ?? true);
    setData((current) => current ? { ...current, username: result.username, closeLauncherOnGameStart: result.closeLauncherOnGameStart } : current);
    showNotice(`Nombre local actualizado a ${result.username}.`, "success");
  }, [data?.closeLauncherOnGameStart, showNotice]);

  const signIn = useCallback(async () => {
    setAccountBusy(true);
    try {
      const next = await signInMicrosoft();
      setAccount(next);
      setSection("account");
      showNotice(`Bienvenido, ${next.minecraftName ?? "cuenta Microsoft"}. NEXA Premium está activo.`);
    } catch (error) {
      showNotice(error instanceof Error ? error.message : "No se pudo iniciar sesión con Microsoft.", "error");
    } finally {
      setAccountBusy(false);
    }
  }, [showNotice]);

  const signOut = useCallback(async () => {
    setAccountBusy(true);
    try {
      const next = await signOutMicrosoft();
      setAccount(next);
      showNotice("Sesión Microsoft cerrada. NEXA volvió al modo local.");
    } catch (error) {
      showNotice(error instanceof Error ? error.message : "No se pudo cerrar la sesión.", "error");
    } finally {
      setAccountBusy(false);
    }
  }, [showNotice]);

  const uploadSkin = useCallback(async (variant: SkinVariant) => {
    setAccountBusy(true);
    try {
      const next = await uploadMicrosoftSkin(variant);
      setAccount(next);
      showNotice("Skin actualizada en tu perfil de Minecraft.");
    } catch (error) {
      showNotice(error instanceof Error ? error.message : "No se pudo actualizar la skin.", "error");
    } finally {
      setAccountBusy(false);
    }
  }, [showNotice]);

  const replaceProfile = useCallback((profile: NexaProfile) => {
    setData((current) => current ? {
      ...current,
      profiles: current.profiles.map((item) => item.id === profile.id
        ? { ...profile, artwork: profile.artwork ?? item.artwork ?? defaultArtworkPlacement }
        : item),
    } : current);
    setSelectedProfileId(profile.id);
  }, []);

  const activeSidebar: SidebarSection = section === "profile" ? "library" : section;
  const title = section === "profile" && selectedProfile ? selectedProfile.name : titleBySection[section];
  const displayUsername = account.premium && account.minecraftName ? account.minecraftName : data?.username ?? "Player";

  return (
    <div className="app-shell">
      <div className="ambient ambient-one" />
      <div className="ambient ambient-two" />
      <Sidebar active={activeSidebar} onChange={navigate} />
      <div className="workspace">
        <Topbar title={title} username={displayUsername} isPremium={account.premium} onOpenAccount={() => navigate("account")} onUpdateLocalUsername={updateLocalUsername} />
        <main className="content-scroll">
          {fatalError && <div className="inline-error"><strong>NEXA no pudo cargar el launcher.</strong><span>{fatalError}</span><button type="button" onClick={() => Promise.all([refresh(), refreshAccount()]).catch((reason: Error) => setFatalError(reason.message))}>REINTENTAR</button></div>}

          {section === "library" && <LibraryPage profiles={profiles} launchingProfileId={launchingProfileId} onCreate={() => navigate("create")} onOpen={openProfile} onPlay={play} />}
          {section === "create" && <CreateProfilePage onCancel={() => navigate("library")} onNotice={showNotice} onCreated={(profile) => { const hydrated = { ...profile, artwork: profile.artwork ?? defaultArtworkPlacement }; setData((current) => current ? { ...current, profiles: [hydrated, ...current.profiles.filter((item) => item.id !== profile.id)] } : current); openProfile(hydrated); }} />}
          {section === "profile" && selectedProfile && <ProfileDetailPage key={selectedProfile.id} profile={selectedProfile} launching={selectedProfileBusy} onLaunch={play} onContent={openContent} onUpdated={replaceProfile} onDeleted={() => { setData((current) => current ? { ...current, profiles: current.profiles.filter((item) => item.id !== selectedProfile.id) } : current); navigate("library"); }} onBack={() => navigate("library")} onNotice={showNotice} />}
          {section === "profile" && !selectedProfile && <div className="page"><div className="empty-state glass-panel"><h2>Perfil no disponible</h2><p>Vuelve a Biblioteca y selecciona un perfil.</p></div></div>}
          {section === "content" && <ContentPage profiles={profiles} initialProfileId={selectedProfileId} onSelectProfile={setSelectedProfileId} onNotice={showNotice} />}
          {section === "account" && <AccountPage account={account} busy={accountBusy} onSignIn={signIn} onSignOut={signOut} onUploadSkin={uploadSkin} />}
          {section === "settings" && <SettingsPage username={data?.username ?? "Player"} closeLauncherOnGameStart={data?.closeLauncherOnGameStart ?? true} version={data?.version ?? "1.0.0"} onUpdated={(username, closeLauncherOnGameStart) => setData((current) => current ? { ...current, username, closeLauncherOnGameStart } : current)} onNotice={showNotice} />}
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
