export type NexaProfile = {
  id: string;
  name: string;
  description?: string | null;
  minecraftVersion: string;
  loader: string;
  loaderVersion?: string | null;
  lastPlayedAt?: string | null;
  memoryMiB?: number | null;
  iconDataUrl?: string | null;
  backgroundDataUrl?: string | null;
};

export type ActiveLaunch = {
  profileId?: string | null;
  pid: number;
  logPath: string;
};

export type BootstrapData = {
  productName: string;
  version: string;
  username: string;
  closeLauncherOnGameStart: boolean;
  profiles: NexaProfile[];
  activeLaunch?: ActiveLaunch | null;
};

export type MinecraftVersionItem = {
  id: string;
  releaseTime: string;
  stable: boolean;
};

export type LoaderVersionItem = {
  version: string;
  stable: boolean;
};

export type CreateProfileRequest = {
  name: string;
  description?: string;
  minecraftVersion: string;
  loader: "Vanilla" | "Fabric" | "Forge" | "NeoForge";
  loaderVersion?: string | null;
  memoryMiB?: number | null;
  iconDataUrl?: string | null;
  backgroundDataUrl?: string | null;
};

export type UpdateProfileRequest = {
  id: string;
  name: string;
  description?: string;
  iconDataUrl?: string | null;
  backgroundDataUrl?: string | null;
  removeIcon: boolean;
  removeBackground: boolean;
};

export type InstalledContentEntry = {
  category: string;
  name: string;
  relativePath: string;
  sizeBytes: number;
  enabled: boolean;
  canToggle: boolean;
  isDirectory: boolean;
};

export type ContentCatalogProject = {
  id: string;
  title: string;
  description: string;
  author: string;
  projectType: "mod" | "resourcepack" | "shader" | "datapack";
  iconUrl?: string | null;
  downloads: number;
};

export type OperationProgress = {
  stage: string;
  completed?: number;
  total?: number;
  percentage?: number;
};

export type BridgeResponse<T = unknown> = {
  id: string;
  ok: boolean;
  result?: T;
  error?: string;
};

export type BridgeEvent<T = unknown> = {
  kind: "event";
  name: string;
  payload: T;
};
