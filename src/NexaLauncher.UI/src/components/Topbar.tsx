import { ChevronDown } from "lucide-react";

type TopbarProps = {
  title: string;
  username: string;
};

export function Topbar({ title, username }: TopbarProps) {
  return (
    <header className="topbar glass-edge">
      <div className="topbar-title">{title}</div>
      <div className="topbar-actions">
        <div className="core-pill"><span className="status-dot" /> NEXA CORE LISTO</div>
        <button className="user-card" type="button" aria-label="Cuenta local">
          <span className="user-avatar"><img src="./brand/nexa-mark.png" alt="" /></span>
          <span className="user-copy">
            <strong>{username || "Player"}</strong>
            <small>Perfil local</small>
          </span>
          <ChevronDown size={15} className="user-chevron" />
        </button>
      </div>
    </header>
  );
}
