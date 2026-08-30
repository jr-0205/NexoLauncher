import { Boxes, Crown, LibraryBig, Plus, Settings2, Sparkles } from "lucide-react";

type Section = "library" | "create" | "content" | "account" | "settings";

type SidebarProps = {
  active: Section;
  onChange(section: Section): void;
};

const items: Array<{ key: Section; label: string; icon: typeof LibraryBig }> = [
  { key: "library", label: "Biblioteca", icon: LibraryBig },
  { key: "create", label: "Crear perfil", icon: Plus },
  { key: "content", label: "Contenido", icon: Boxes },
  { key: "account", label: "Cuenta", icon: Crown },
  { key: "settings", label: "Configuración", icon: Settings2 },
];

export function Sidebar({ active, onChange }: SidebarProps) {
  return (
    <aside className="sidebar glass-edge">
      <button className="brand-button" onClick={() => onChange("library")} aria-label="NEXA Client">
        <img src="./brand/nexa-mark.png" alt="NEXA" className="brand-mark" />
      </button>

      <nav className="sidebar-nav" aria-label="Navegación principal">
        {items.map(({ key, label, icon: Icon }) => (
          <button
            key={key}
            type="button"
            className={`nav-button ${active === key ? "active" : ""}`}
            onClick={() => onChange(key)}
            title={label}
            aria-label={label}
          >
            <Icon size={20} strokeWidth={1.8} />
            <span className="nav-tooltip">{label}</span>
          </button>
        ))}
      </nav>

      <div className="sidebar-status" title="NEXA Core listo">
        <Sparkles size={16} />
      </div>
    </aside>
  );
}
