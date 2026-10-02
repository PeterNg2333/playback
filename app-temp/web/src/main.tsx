import { QueryClientProvider } from "@tanstack/react-query";
import { createRoot } from "react-dom/client";
import "./styles/app.css";
import { queryClient } from "./lib/queryClient";
import { PlaybackPage } from "./pages/PlaybackPage";

createRoot(document.getElementById("root")!).render(
  <QueryClientProvider client={queryClient}>
    <PlaybackPage />
  </QueryClientProvider>,
);
