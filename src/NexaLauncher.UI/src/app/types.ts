export type NexaProfile = {
  id: string;
  name: string;
  description?: string | null;
  minecraftVersion: string;
  loader: string;
  loaderVersion?: string | null;
  lastPlayedAt?: string | null;
  iconDataUrl?: string | null;
  backgroundDataUrl?: string | null;
};

export type BootstrapData = {
  productName: string;
  version: string;
  username: string;
  profiles: NexaProfile[];
};

export type BridgeResponse<T = unknown> = {
  id: string;
  ok: boolean;
  result?: T;
  error?: string;
};
