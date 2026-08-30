import { Check, ChevronDown, Loader2, UserRound } from "lucide-react";
import { useEffect, useRef, useState } from "react";

type TopbarProps = {
  title: string;
  username: string;
  isPremium?: boolean;
  onUpdateLocalUsername(username: string): Promise<void>;
};

export function Topbar({ title, username, isPremium = false, onUpdateLocalUsername }: TopbarProps) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(username || "Player");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const wrapper = useRef<HTMLDivElement>(null);

  useEffect(() => setDraft(username || "Player"), [username]);

  useEffect(() => {
    if (!open) return;
    const closeOutside = (event: PointerEvent) => {
      if (!wrapper.current?.contains(event.target as Node)) setOpen(false);
    };
    const closeEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    window.addEventListener("pointerdown", closeOutside);
    window.addEventListener("keydown", closeEscape);
    return () => {
      window.removeEventListener("pointerdown", closeOutside);
      window.removeEventListener("keydown", closeEscape);
    };
  }, [open]);

  async function save() {
    const value = draft.trim();
    if (isPremium) return;
    if (!/^[A-Za-z0-9_]{3,16}$/.test(value)) {
      setError("Usa de 3 a 16 caracteres: letras, números o guion bajo.");
      return;
    }
    setSaving(true);
    setError(null);
    try {
      await onUpdateLocalUsername(value);
      setOpen(false);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo cambiar el nombre de jugador.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <header className="topbar glass-edge">
      <div className="topbar-title">{title}</div>
      <div className="topbar-actions">
        <div className="core-pill"><span className="status-dot" /> NEXA CORE LISTO</div>
        <div className="user-menu-wrap" ref={wrapper}>
          <button className={`user-card ${open ? "active" : ""}`} type="button" aria-label="Cuenta local" aria-expanded={open} onClick={() => setOpen((value) => !value)}>
            <span className="user-avatar"><img src="./brand/nexa-mark.png" alt="" /></span>
            <span className="user-copy">
              <strong>{username || "Player"}</strong>
              <small>{isPremium ? "Cuenta premium" : "Perfil local"}</small>
            </span>
            <ChevronDown size={15} className={`user-chevron ${open ? "open" : ""}`} />
          </button>

          {open && (
            <div className="user-popover glass-panel" role="dialog" aria-label="Perfil de jugador">
              <div className="user-popover-head">
                <span className="user-popover-mark"><img src="./brand/nexa-mark.png" alt="" /></span>
                <div><span className="eyebrow">{isPremium ? "CUENTA NEXA" : "PERFIL LOCAL · NO PREMIUM"}</span><strong>{username || "Player"}</strong></div>
              </div>

              {isPremium ? (
                <div className="user-premium-note"><Check size={16} /><span>El nombre proviene de la cuenta autenticada y no se edita localmente.</span></div>
              ) : (
                <>
                  <p className="user-popover-description">Mientras uses un perfil local puedes cambiar aquí el nombre que NEXA utilizará para las sesiones offline.</p>
                  <label className="field-label">NOMBRE DE JUGADOR
                    <div className="user-name-input"><UserRound size={15} /><input value={draft} maxLength={16} onChange={(event) => { setDraft(event.target.value); setError(null); }} onKeyDown={(event) => { if (event.key === "Enter") save(); }} autoFocus /></div>
                  </label>
                  {error && <div className="user-popover-error">{error}</div>}
                  <div className="user-popover-actions">
                    <button className="ghost-button" type="button" onClick={() => { setDraft(username || "Player"); setError(null); setOpen(false); }}>CANCELAR</button>
                    <button className="primary-button" type="button" disabled={saving || draft.trim() === username} onClick={save}>{saving ? <Loader2 className="spin" size={15} /> : <Check size={15} />} GUARDAR</button>
                  </div>
                </>
              )}
            </div>
          )}
        </div>
      </div>
    </header>
  );
}
