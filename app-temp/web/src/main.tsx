import { QueryClientProvider } from "@tanstack/react-query";
import { createRoot } from "react-dom/client";
import "./styles/base.css";
import "./styles/layout.css";
import "./styles/notes.css";
import "./styles/transcript.css";
import "./styles/controls.css";
import "./styles/activity.css";
import "./styles/overlays.css";
import { queryClient } from "./lib/queryClient";
import { PlaybackPage } from "./pages/PlaybackPage";

createRoot(document.getElementById("root")!).render(
  <QueryClientProvider client={queryClient}>
    <PlaybackPage />
  </QueryClientProvider>,
);
