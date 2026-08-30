import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./app/App";
import "./styles/index.css";
import "./styles/workflows.css";
import "./styles/brand.css";
import "./styles/interaction.css";
import "./styles/profile-tools.css";
import "./styles/build-manager.css";
import "./styles/build-family.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
