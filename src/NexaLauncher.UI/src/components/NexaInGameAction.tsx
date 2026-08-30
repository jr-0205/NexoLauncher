import { Check, Gamepad2, Loader2 } from "lucide-react";
import { useEffect, useState } from "react";
import { getNexaInGameStatus, installNexaInGame } from "../app/nexa-bridge";
import type { NexaInGameStatus, NexaProfile } from "../app/types";

type Props = {
  profile: NexaProfile;
  launching: boolean;
  onNotice(message: string, kind?: "success" | "error"): void;
};

export function NexaInGameAction({ profile, launching, onNotice }: Props) {
  const [status, setStatus] = useState<NexaInGameStatus | null>(null);
  const [busy, setBusy] = useState(false);

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

  async function install() {
    if (busy || launching) return;
    if (!status) return;

    if (status.installed) {
      onNotice("NEXA In-Game ya está instalado. Inicia Minecraft y pulsa Shift derecho.", "success");
      return;
    }

    if (!status.available) {
      onNotice(status.message, "error");
      return;
    }

    setBusy(true);
    try {
      const result = await installNexaInGame(profile.id);
      const refreshed = await getNexaInGameStatus(profile.id);
      setStatus(refreshed);
      const cache = result.usedCache ? " Se reutilizó la caché verificada." : "";
      onNotice(`NEXA In-Game ${result.version} instalado. Shift derecho ya está listo.${cache}`, "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo instalar NEXA In-Game.", "error");
    } finally {
      setBusy(false);
    }
  }

  const label = busy
    ? "INSTALANDO NEXA IN-GAME…"
    : status?.installed
      ? "NEXA IN-GAME · LISTO"
      : status?.available
        ? "AÑADIR NEXA IN-GAME"
        : status
          ? "NEXA IN-GAME · BUILD PENDIENTE"
          : "NEXA IN-GAME";

  return (
    <button
      className={`secondary-button ${status?.installed ? "boost-active-button" : ""}`}
      type="button"
      disabled={busy || launching || !status}
      title={status?.message ?? "Comprobando una build compatible con este perfil…"}
      onClick={install}
    >
      {busy ? <Loader2 className="spin" size={16} /> : status?.installed ? <Check size={16} /> : <Gamepad2 size={16} />}
      {label}
    </button>
  );
}
