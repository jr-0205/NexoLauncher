import { AlertTriangle, Info, ShieldAlert, X } from "lucide-react";

export type NexaDialogTone = "info" | "warning" | "danger";

type Props = {
  open: boolean;
  tone?: NexaDialogTone;
  title: string;
  description: string;
  confirmLabel?: string;
  cancelLabel?: string;
  busy?: boolean;
  onConfirm(): void;
  onCancel(): void;
};

export function NexaDialog({ open, tone = "info", title, description, confirmLabel = "ACEPTAR", cancelLabel = "CANCELAR", busy = false, onConfirm, onCancel }: Props) {
  if (!open) return null;
  const Icon = tone === "danger" ? ShieldAlert : tone === "warning" ? AlertTriangle : Info;
  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && !busy && onCancel()}>
      <section className={`nexa-dialog ${tone}`} role="dialog" aria-modal="true" aria-labelledby="nexa-dialog-title">
        <div className="dialog-glow" />
        <header className="dialog-header">
          <div className="dialog-icon"><Icon size={20} /></div>
          <button className="icon-button" type="button" disabled={busy} onClick={onCancel} aria-label="Cerrar"><X size={17} /></button>
        </header>
        <div className="dialog-copy">
          <span className="eyebrow">NEXA CLIENT</span>
          <h2 id="nexa-dialog-title">{title}</h2>
          <p>{description}</p>
        </div>
        <footer className="dialog-actions">
          <button className="ghost-button" type="button" disabled={busy} onClick={onCancel}>{cancelLabel}</button>
          <button className={tone === "danger" ? "danger-button" : "primary-button"} type="button" disabled={busy} onClick={onConfirm}>{confirmLabel}</button>
        </footer>
      </section>
    </div>
  );
}
