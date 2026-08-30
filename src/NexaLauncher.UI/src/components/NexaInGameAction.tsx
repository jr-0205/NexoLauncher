import { Check, Gamepad2, Loader2, RefreshCw } from "lucide-react";
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

  async function installOrRepair() {
    if (busy || launching || !status) return;

    if (!status.available) {
      onNotice(status.installed
        ? "NEXA In-Game está instalado, pero no hay una build publicada disponible para reinstalar o actualizar ahora mismo."
        : status.message, "error");
      return;
    }

    const wasInstalled = status.installed;
    setBusy(true);
    try {
      const result = await installNexaInGame(profile.id);
      const refreshed = await getNexaInGameStatus(profile.id);
      setStatus(refreshed);
      const cache = result.usedCache ? " Se reutilizó la caché verificada." : "";
      onNotice(
        wasInstalled
          ? `NEXA In-Game ${result.version} reinstalado/actualizado correctamente.${cache}`
          : `NEXA In-Game ${result.version} instalado. Shift derecho ya está listo.${cache}`,
        "success",
      );
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo instalar NEXA In-Game.", "error");
    } finally {
      setBusy(false);
    }
  }

  const label = busy
    ? status?.installed ? "REINSTALANDO / ACTUALIZANDO…" : "INSTALANDO NEXA IN-GAME…"
    : status?.installed
      ? "NEXA IN-GAME · LISTO"
      : status?.available
        ? "AÑADIR NEXA IN-GAME"
        : status
          ? "NEXA IN-GAME · BUILD PENDIENTE"
          : "NEXA IN-GAME";

  return (
    <div className="ingame-action-stack">
      <button
        className={`secondary-button ${status?.installed ? "boost-active-button" : ""}`}
        type="button"
        disabled={busy || launching || !status || (!status.installed && !status.available)}
        title={status?.message ?? "Comprobando una build compatible con este perfil…"}
        onClick={status?.installed ? undefined : installOrRepair}
      >
        {busy ? <Loader2 className="spin" size={16} /> : status?.installed ? <Check size={16} /> : <Gamepad2 size={16} />}
        {label}
      </button>
      {status?.installed && (
        <button
          className="ghost-button ingame-maintenance-button"
          type="button"
          disabled={busy || launching || !status.available}
          title={status.available
            ? "Instala la build publicada más reciente compatible. Si es la misma, la reinstala y vuelve a verificarla."
            : "No hay una build publicada disponible para mantenimiento en este momento."}
          onClick={installOrRepair}
        >
          {busy ? <Loader2 className="spin" size={14} /> : <RefreshCw size={14} />}
          REINSTALAR / ACTUALIZAR
        </button>
      )}
    </div>
  );
}
