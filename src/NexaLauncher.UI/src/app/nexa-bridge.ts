import type { BootstrapData, BridgeResponse } from "./types";

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
let listening = false;

function ensureListener() {
  const webview = window.chrome?.webview;
  if (!webview || listening) return;
  listening = true;
  webview.addEventListener("message", (event) => {
    const response = event.data as BridgeResponse;
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
  profiles: [],
};

export async function bootstrap(): Promise<BootstrapData> {
  if (!isNativeHost()) return previewData;
  return invoke<BootstrapData>("app.bootstrap");
}
