import { useEffect, useState } from "react";
import { Check, ExternalLink, Loader2, Save, ShieldCheck } from "lucide-react";
import { updateSettings } from "../app/nexa-bridge";
import { nexaWordmarkDataUrl } from "../brand/nexa-wordmark";

type Props = {
  username: string;
  closeLauncherOnGameStart: boolean;
  version: string;
  onUpdated(username: string, closeLauncherOnGameStart: boolean): void;
  onNotice(message: string, kind?: "success" | "error"): void;
};

export function SettingsPage({ username, closeLauncherOnGameStart, version, onUpdated, onNotice }: Props) {
  const [playerName, setPlayerName] = useState(username);
  const [closeOnLaunch, setCloseOnLaunch] = useState(closeLauncherOnGameStart);
  const [saving, setSaving] = useState(false);

  useEffect(() => setPlayerName(username), [username]);
  useEffect(() => setCloseOnLaunch(closeLauncherOnGameStart), [closeLauncherOnGameStart]);

  async function save() {
    setSaving(true);
    try {
      const result = await updateSettings(playerName, closeOnLaunch);
      setPlayerName(result.username);
      setCloseOnLaunch(result.closeLauncherOnGameStart);
      onUpdated(result.username, result.closeLauncherOnGameStart);
      onNotice("Configuración guardada.", "success");
    } catch (error) {
      onNotice(error instanceof Error ? error.message : "No se pudo guardar la configuración.", "error");
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="page settings-page">
      <div className="hero-row settings-heading">
        <div>
          <span className="eyebrow">NEXA CLIENT</span>
          <h1>Configuración</h1>
          <p>Ajustes globales del launcher. Java sigue siendo automático por perfil.</p>
        </div>
        <button className="primary-button" type="button" disabled={saving} onClick={save}>{saving ? <Loader2 className="spin" size={16} /> : <Save size={16} />} GUARDAR</button>
      </div>

      <div className="settings-grid">
        <article className="settings-card glass-panel">
          <span className="eyebrow">JUGADOR</span>
          <h2>Perfil local</h2>
          <p>Este nombre se usa para sesiones locales/offline mientras la autenticación Microsoft permanece separada.</p>
          <label className="field-label">NOMBRE DE JUGADOR<input className="nexa-input" maxLength={16} value={playerName} onChange={(event) => setPlayerName(event.target.value)} placeholder="Player" /></label>
        </article>

        <article className="settings-card glass-panel">
          <span className="eyebrow">COMPORTAMIENTO</span>
          <h2>Al iniciar Minecraft</h2>
          <p>Controla qué ocurre con NEXA cuando el juego se inicia correctamente.</p>
          <button className={`switch-row ${closeOnLaunch ? "enabled" : ""}`} type="button" onClick={() => setCloseOnLaunch((value) => !value)}>
            <span className="switch-track"><span /></span>
            <span><strong>Cerrar launcher al iniciar</strong><small>{closeOnLaunch ? "NEXA se cerrará cuando Minecraft arranque." : "NEXA permanecerá abierto durante la sesión."}</small></span>
            {closeOnLaunch && <Check size={17} />}
          </button>
        </article>

        <article className="settings-card glass-panel settings-wide about-react-card">
          <div className="about-mark"><img src="./brand/nexa-mark.png" alt="NEXA" /></div>
          <div className="about-copy">
            <img className="about-wordmark" src={nexaWordmarkDataUrl} alt="NEXA Client" />
            <span className="eyebrow">ACERCA DE NEXA</span>
            <h2>NEXA Client <small>{version}</small></h2>
            <p>Cliente de Minecraft para Windows. Backend .NET, interfaz React y perfiles físicamente aislados por GUID.</p>
            <div className="trust-row"><ShieldCheck size={17} /><span>Sin telemetría, anuncios ni acceso directo de JavaScript al sistema de archivos.</span></div>
            <div className="about-actions">
              <a className="secondary-button link-button" href="https://github.com/jr-0205" target="_blank" rel="noreferrer"><ExternalLink size={15} /> GITHUB DEL CREADOR</a>
              <a className="ghost-button link-button" href="https://chatgpt.com/download/" target="_blank" rel="noreferrer"><ExternalLink size={15} /> CHATGPT</a>
            </div>
          </div>
        </article>
      </div>
    </section>
  );
}
