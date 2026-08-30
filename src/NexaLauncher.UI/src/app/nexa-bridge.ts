import type {
  ArtworkPlacementEntry,
  BoostApplyResult,
  BoostRemoveResult,
  BoostStatus,
  BootstrapData,
  BridgeEvent,
  BridgeResponse,
  ContentCatalogProject,
  CreateProfileRequest,
  InstalledContentEntry,
  LoaderVersionItem,
  MinecraftVersionItem,
  NexaAccountState,
  NexaInGameBuildGenerateResult,
  NexaInGameBuildLibrary,
  NexaInGameInstallResult,
  NexaInGameStatus,
  NexaProfile,
  ProfileArtworkPlacement,
  ProfileLiveLogs,
  UpdateProfileRequest,
} from "./types";

type WebViewMessageEvent = { data: unknown };
type WebViewBridge = {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: WebViewMessageEvent) => void): void;
};
type NativeBridgeEvent = BridgeEvent & { event?: string };

declare global {
  interface Window {
    chrome?: { webview?: WebViewBridge };
  }
}

const pending = new Map<string, { resolve(value: unknown): void; reject(error: Error): void }>();
const listeners = new Map<string, Set<(payload: unknown) => void>>();
let listening = false;

function ensureListener() {
  const webview = window.chrome?.webview;
  if (!webview || listening) return;
  listening = true;
  webview.addEventListener("message", (event) => {
    const value = event.data as BridgeResponse | NativeBridgeEvent;
    const response = value as BridgeResponse;
    const bridgeEvent = value as NativeBridgeEvent;
    const eventName = bridgeEvent.name ?? bridgeEvent.event;

    if ((!response?.id || response.id.length === 0) && eventName) {
      listeners.get(eventName)?.forEach((listener) => listener(bridgeEvent.payload));
      return;
    }

    if (!response?.id) return;
    const request = pending.get(response.id);
    if (!request) return;
    pending.delete(response.id);
    if (response.ok) request.resolve(response.result);
    else request.reject(new Error(response.error ?? "NEXA no pudo completar la operación."));
  });
}

export function isNativeHost() {
  return Boolean(window.chrome?.webview);
}

export function onBridgeEvent<T>(name: string, listener: (payload: T) => void) {
  ensureListener();
  const set = listeners.get(name) ?? new Set<(payload: unknown) => void>();
  const wrapped = listener as (payload: unknown) => void;
  set.add(wrapped);
  listeners.set(name, set);
  return () => {
    set.delete(wrapped);
    if (set.size === 0) listeners.delete(name);
  };
}

export function invoke<T>(method: string, payload: Record<string, unknown> = {}): Promise<T> {
  const webview = window.chrome?.webview;
  if (!webview) return Promise.reject(new Error("NEXA Desktop Bridge no está disponible."));
  ensureListener();
  const id = crypto.randomUUID();
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (value: unknown) => void, reject });
    webview.postMessage({ id, method, payload });
  });
}

const previewData: BootstrapData = {
  productName: "NEXA Client",
  version: "React migration preview",
  username: "Player",
  closeLauncherOnGameStart: true,
  profiles: [],
  activeLaunch: null,
};

const previewAccount: NexaAccountState = {
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
  message: "El inicio de sesión Microsoft está disponible dentro de NEXA Desktop.",
};

export async function bootstrap(): Promise<BootstrapData> {
  if (!isNativeHost()) return previewData;
  return invoke<BootstrapData>("app.bootstrap");
}

export const listProfiles = () => invoke<NexaProfile[]>("profiles.list");
export const getMinecraftVersions = () => invoke<MinecraftVersionItem[]>("catalog.minecraftVersions");
export const getLoaderVersions = (minecraftVersion: string, loader: string) =>
  invoke<LoaderVersionItem[]>("catalog.loaderVersions", { minecraftVersion, loader });
export const createProfile = (request: CreateProfileRequest) =>
  invoke<NexaProfile>("profiles.create", request as unknown as Record<string, unknown>);
export const updateProfile = (request: UpdateProfileRequest) =>
  invoke<NexaProfile>("profiles.update", request as unknown as Record<string, unknown>);
export const deleteProfile = (id: string) => invoke<{ deleted: boolean }>("profiles.delete", { id });
export const openProfileFolder = (id: string) => invoke<{ opened: boolean }>("profiles.openFolder", { id });
export const getProfileLiveLogs = (id: string) => invoke<ProfileLiveLogs>("profiles.liveLogs", { id });
export const launchProfile = (id: string) => invoke<{ pid: number; logPath: string; profile: NexaProfile }>("profiles.launch", { id });
export const stopLaunch = () => invoke<{ stopped: boolean }>("profiles.stop");

export async function getAccountStatus(): Promise<NexaAccountState> {
  if (!isNativeHost()) return previewAccount;
  return invoke<NexaAccountState>("account.status");
}
export const signInMicrosoft = () => invoke<NexaAccountState>("account.signIn");
export const signOutMicrosoft = () => invoke<NexaAccountState>("account.signOut");
export const uploadMicrosoftSkin = (variant: "classic" | "slim") =>
  invoke<NexaAccountState>("account.skin.upload", { variant });

export const listInstalledContent = (id: string) => invoke<InstalledContentEntry[]>("content.list", { id });
export const toggleInstalledContent = (id: string, entry: InstalledContentEntry) =>
  invoke<InstalledContentEntry>("content.toggle", { id, entry });
export const deleteInstalledContent = (id: string, entry: InstalledContentEntry) =>
  invoke<{ deleted: boolean }>("content.delete", { id, entry });
export const openInstalledContent = (id: string, entry: InstalledContentEntry) =>
  invoke<{ opened: boolean }>("content.open", { id, entry });
export const searchContent = (id: string, query: string, projectType: ContentCatalogProject["projectType"]) =>
  invoke<ContentCatalogProject[]>("content.search", { id, query, projectType });
export const installContent = (id: string, project: ContentCatalogProject) =>
  invoke<{ filesInstalled: number; fileNames: string[]; installed: InstalledContentEntry[] }>("content.install", { id, project });

export const getBoostStatus = (id: string) => invoke<BoostStatus>("boost.status", { id });
export const applyBoost = (id: string) => invoke<BoostApplyResult>("boost.apply", { id });
export const removeBoost = (id: string) => invoke<BoostRemoveResult>("boost.remove", { id });

export const getNexaInGameStatus = (id: string) => invoke<NexaInGameStatus>("ingame.status", { id });
export const installNexaInGame = (id: string) => invoke<NexaInGameInstallResult>("ingame.install", { id });
export const getNexaInGameBuildLibrary = () => invoke<NexaInGameBuildLibrary>("ingame.builds.status");
export const generateNexaInGameBuilds = () => invoke<NexaInGameBuildGenerateResult>("ingame.builds.generate");
export const generateNexaInGameBuild = (minecraftVersion: string, loader: string) =>
  invoke<{ published: boolean; minecraftVersion: string; loader: string; failureCount: number; failures: { minecraftVersion: string; loader: string; message: string }[] }>(
    "ingame.builds.generateOne",
    { minecraftVersion, loader },
  );
export const openNexaInGameBuildFolder = () => invoke<{ opened: boolean; path: string }>("ingame.builds.openFolder");

export const listArtworkPlacements = () => invoke<ArtworkPlacementEntry[]>("artwork.list");
export const updateArtworkPlacement = (id: string, artwork: ProfileArtworkPlacement) =>
  invoke<ArtworkPlacementEntry>("artwork.update", { id, ...artwork });

export const updateSettings = (username: string, closeLauncherOnGameStart: boolean) =>
  invoke<{ username: string; closeLauncherOnGameStart: boolean }>("settings.update", { username, closeLauncherOnGameStart });
