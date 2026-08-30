import { useEffect, useState } from "react";
import { bootstrap } from "./nexa-bridge";
import type { BootstrapData } from "./types";
import { Sidebar } from "../components/Sidebar";
import { Topbar } from "../components/Topbar";
import { LibraryPage } from "../pages/LibraryPage";

type Section = "library" | "create" | "content" | "settings";

const titleBySection: Record<Section, string> = {
  library: "Biblioteca",
  create: "Crear perfil",
  content: "Contenido",
  settings: "Configuración",
};

export default function App() {
  const [section, setSection] = useState<Section>("library");
  const [data, setData] = useState<BootstrapData | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    bootstrap().then(setData).catch((reason: Error) => setError(reason.message));
  }, []);

  return (
    <div className="app-shell">
      <div className="ambient ambient-one" />
      <div className="ambient ambient-two" />
      <Sidebar active={section} onChange={setSection} />
      <div className="workspace">
        <Topbar title={titleBySection[section]} username={data?.username ?? "Player"} />
        <main className="content-scroll">
          {error && <div className="inline-error">NEXA no pudo cargar los datos del launcher: {error}</div>}
          {section === "library" && <LibraryPage profiles={data?.profiles ?? []} onCreate={() => setSection("create")} />}
          {section !== "library" && (
            <section className="page placeholder-page">
              <span className="eyebrow">MIGRACIÓN REACT EN CURSO</span>
              <h1>{titleBySection[section]}</h1>
              <p>Esta pantalla será la siguiente en conectarse al backend C# de NEXA. El shell y la Biblioteca ya usan la nueva arquitectura.</p>
            </section>
          )}
        </main>
      </div>
    </div>
  );
}
