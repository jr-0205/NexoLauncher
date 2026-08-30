import { Check, Crown, Loader2, LogIn, LogOut, ShieldCheck, Shirt, Sparkles, Upload, UserRound } from "lucide-react";
import { useEffect, useState } from "react";
import type { NexaAccountState } from "../app/types";

export type SkinVariant = "classic" | "slim";

type Props = {
  account: NexaAccountState;
  busy: boolean;
  onSignIn(): Promise<void>;
  onSignOut(): Promise<void>;
  onUploadSkin(variant: SkinVariant): Promise<void>;
};

export function AccountPage({ account, busy, onSignIn, onSignOut, onUploadSkin }: Props) {
  const initialVariant: SkinVariant = account.activeSkinVariant?.toLowerCase() === "slim" ? "slim" : "classic";
  const [variant, setVariant] = useState<SkinVariant>(initialVariant);

  useEffect(() => {
    setVariant(account.activeSkinVariant?.toLowerCase() === "slim" ? "slim" : "classic");
  }, [account.activeSkinVariant]);

  if (!account.signedIn) {
    return (
      <section className="page account-page">
        <div className="account-landing glass-panel">
          <div className="account-landing-copy">
            <span className="eyebrow">NEXA PREMIUM · CUENTA MICROSOFT</span>
            <h1>Tu identidad de Minecraft, integrada en NEXA.</h1>
            <p>
              NEXA sigue funcionando como launcher local sin cuenta. Al conectar Microsoft se habilita la experiencia premium:
              identidad oficial de Minecraft Java, sesiones online y gestión de apariencia desde el launcher.
            </p>
            <button className="primary-button account-login-button" type="button" disabled={busy || !account.configured} onClick={onSignIn}>
              {busy ? <Loader2 className="spin" size={17} /> : <LogIn size={17} />}
              {busy ? "CONECTANDO…" : "CONTINUAR CON MICROSOFT"}
            </button>
            {!account.configured && (
              <div className="account-config-warning">
                <ShieldCheck size={16} />
                <span>El módulo está preparado, pero esta build necesita un Client ID público de Microsoft autorizado para NEXA.</span>
              </div>
            )}
            {account.message && <p className="account-message">{account.message}</p>}
          </div>

          <div className="account-feature-grid">
            <Feature icon={<ShieldCheck size={20} />} title="Autenticación segura" text="NEXA abre el navegador del sistema. Tu contraseña nunca entra al launcher ni al WebView." />
            <Feature icon={<UserRound size={20} />} title="Identidad oficial" text="Nombre y UUID provienen del perfil real de Minecraft y se usan automáticamente al iniciar el juego." />
            <Feature icon={<Shirt size={20} />} title="Skins premium" text="Selecciona una skin PNG, valida el modelo Classic/Slim y publícala en tu perfil oficial desde NEXA." />
          </div>
        </div>
      </section>
    );
  }

  const activeCape = account.capes.find((cape) => cape.active);

  return (
    <section className="page account-page">
      <div className="account-heading">
        <div>
          <span className="eyebrow">NEXA PREMIUM</span>
          <h1>Cuenta</h1>
          <p>Identidad oficial de Minecraft y apariencia vinculada a tu cuenta Microsoft.</p>
        </div>
        <div className="premium-badge"><Crown size={15} /> PREMIUM ACTIVO</div>
      </div>

      <div className="account-dashboard">
        <article className="account-profile-card glass-panel">
          <div className="account-profile-mark">
            <img src="./brand/original/NEXA%20N.png" alt="NEXA" />
          </div>
          <div className="account-profile-copy">
            <span className="eyebrow">MINECRAFT: JAVA EDITION</span>
            <h2>{account.minecraftName ?? "Minecraft Player"}</h2>
            <p>{account.microsoftAccount ?? "Cuenta Microsoft conectada"}</p>
            <code>{formatUuid(account.minecraftId)}</code>
          </div>
          <div className="account-verified"><Check size={15} /> Licencia verificada</div>
        </article>

        <article className="skin-manager glass-panel">
          <div className="skin-manager-copy">
            <span className="eyebrow">APARIENCIA</span>
            <h2>Skin de Minecraft</h2>
            <p>El archivo se selecciona mediante una ventana nativa de Windows. La ruta local nunca se expone a React.</p>

            <div className="skin-variant-picker" role="group" aria-label="Modelo de skin">
              <button type="button" className={variant === "classic" ? "active" : ""} onClick={() => setVariant("classic")} disabled={busy}>
                CLASSIC <small>Steve · brazos de 4 px</small>
              </button>
              <button type="button" className={variant === "slim" ? "active" : ""} onClick={() => setVariant("slim")} disabled={busy}>
                SLIM <small>Alex · brazos de 3 px</small>
              </button>
            </div>

            <button className="primary-button skin-upload-button" type="button" disabled={busy} onClick={() => onUploadSkin(variant)}>
              {busy ? <Loader2 className="spin" size={16} /> : <Upload size={16} />}
              {busy ? "ACTUALIZANDO…" : "CAMBIAR SKIN"}
            </button>
            <span className="skin-upload-hint">PNG · 64×64 recomendado · máximo 1 MB</span>
          </div>

          <div className="skin-preview-shell">
            {account.activeSkinUrl ? (
              <img className="skin-texture-preview" src={account.activeSkinUrl} alt={`Skin activa de ${account.minecraftName ?? "Minecraft"}`} />
            ) : (
              <div className="skin-preview-empty"><Shirt size={34} /><span>No hay skin activa disponible.</span></div>
            )}
            <div className="skin-preview-meta">
              <span><Sparkles size={14} /> SKIN ACTIVA</span>
              <strong>{account.activeSkinVariant?.toUpperCase() ?? "CLASSIC"}</strong>
            </div>
          </div>
        </article>

        <article className="account-security-card glass-panel">
          <ShieldCheck size={22} />
          <div>
            <span className="eyebrow">SEGURIDAD DE SESIÓN</span>
            <h3>Credenciales fuera de la interfaz web</h3>
            <p>Los tokens de Microsoft/Xbox/Minecraft permanecen en la capa nativa. React sólo recibe nombre, UUID, estado premium y metadatos públicos del perfil.</p>
          </div>
        </article>

        <article className="account-cape-card glass-panel">
          <span className="eyebrow">CAPA</span>
          <h3>{activeCape?.alias ?? "Sin capa activa"}</h3>
          <p>{account.capes.length > 0 ? `${account.capes.length} capa(s) detectadas en tu perfil.` : "Minecraft no devolvió capas para esta cuenta."}</p>
          {activeCape?.url && <img src={activeCape.url} alt={activeCape.alias} />}
        </article>
      </div>

      <div className="account-danger-row">
        <div>
          <strong>Cerrar sesión en NEXA</strong>
          <span>Elimina la cuenta de la caché local protegida y vuelve al modo no premium.</span>
        </div>
        <button className="ghost-button" type="button" disabled={busy} onClick={onSignOut}><LogOut size={15} /> CERRAR SESIÓN</button>
      </div>
    </section>
  );
}

function Feature({ icon, title, text }: { icon: React.ReactNode; title: string; text: string }) {
  return <div className="account-feature"><span>{icon}</span><div><strong>{title}</strong><p>{text}</p></div></div>;
}

function formatUuid(id?: string | null) {
  if (!id || id.length !== 32) return id ?? "UUID no disponible";
  return `${id.slice(0, 8)}-${id.slice(8, 12)}-${id.slice(12, 16)}-${id.slice(16, 20)}-${id.slice(20)}`;
}
