import type {
  BootstrapData,
  BridgeEvent,
  BridgeResponse,
  ContentCatalogProject,
  CreateProfileRequest,
  InstalledContentEntry,
  LoaderVersionItem,
  MinecraftVersionItem,
  NexaProfile,
  UpdateProfileRequest,
} from "./types";

type WebViewMessageEvent = { data: unknown };
type WebViewBridge = {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: WebViewMessageEvent) => void): void;
};

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
    const value = event.data as BridgeResponse | BridgeEvent;
    if ((value as BridgeEvent)?.kind === "event") {
      const bridgeEvent = value as BridgeEvent;
      listeners.get(bridgeEvent.name)?.forEach((listener) => listener(bridgeEvent.payload));
      return;
    }

    const response = value as BridgeResponse;
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
export const launchProfile = (id: string) => invoke<{ pid: number; logPath: string; profile: NexaProfile }>("profiles.launch", { id });
export const stopLaunch = () => invoke<{ stopped: boolean }>("profiles.stop");

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

export const updateSettings = (username: string, closeLauncherOnGameStart: boolean) =>
  invoke<{ username: string; closeLauncherOnGameStart: boolean }>("settings.update", { username, closeLauncherOnGameStart });
