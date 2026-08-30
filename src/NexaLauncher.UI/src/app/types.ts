export type ProfileArtworkPlacement = {
  iconPositionX: number;
  iconPositionY: number;
  iconFit: "cover" | "contain";
  iconZoom: number;
  backgroundPositionX: number;
  backgroundPositionY: number;
  backgroundFit: "cover" | "contain";
  backgroundZoom: number;
};

export const defaultArtworkPlacement: ProfileArtworkPlacement = {
  iconPositionX: 50,
  iconPositionY: 50,
  iconFit: "contain",
  iconZoom: 100,
  backgroundPositionX: 50,
  backgroundPositionY: 50,
  backgroundFit: "cover",
  backgroundZoom: 100,
};

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
  artwork?: ProfileArtworkPlacement;
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

export type ArtworkPlacementEntry = {
  id: string;
  artwork: ProfileArtworkPlacement;
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

export type BoostComponent = {
  id: string;
  name: string;
  purpose: string;
};

export type BoostStatus = {
  supported: boolean;
  applied: boolean;
  visualApplied: boolean;
  profileId: string;
  minecraftVersion: string;
  loader: string;
  components: BoostComponent[];
};

export type BoostApplyResult = {
  applied: boolean;
  reapplied: boolean;
  filesInstalled: number;
  installedFiles: string[];
  skippedComponents: string[];
  presetChanges: string[];
  particleCoreConfigured: boolean;
  note?: string | null;
};

export type BoostRemoveResult = {
  applied: boolean;
  filesRemoved: number;
  valuesRestored: number;
  preserved: string[];
};

export type NexaInGameStatus = {
  installed: boolean;
  available: boolean;
  profileId: string;
  minecraftVersion: string;
  loader: string;
  version?: string | null;
  fileName?: string | null;
  catalogStatus: "installed" | "published" | "planned" | "unavailable";
  message: string;
};

export type NexaInGameInstallResult = {
  installed: boolean;
  version: string;
  fileName: string;
  usedCache: boolean;
  dependenciesInstalled: string[];
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
