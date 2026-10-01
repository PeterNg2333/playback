import { useEffect, useState } from "react";
import { usePlaybackStore } from "../../pages/store";
import type { Evidence, Session } from "../../types/api";

// Opens a cited source: a web page in a new tab, or the transcript passage or
// teaching material it points to, once the transcript view is showing.
export function useRevealSource(session: Session | null) {
  const [request, setRequest] = useState<{
    sessionId: string;
    evidence: Evidence;
  } | null>(null);
  const view = usePlaybackStore((state) => state.view);
  const transcriptView = usePlaybackStore((state) => state.transcriptView);

  useEffect(() => {
    if (!request) return;
    if (request.sessionId !== session?.id) {
      setRequest(null);
      return;
    }
    if (view !== "transcript" || transcriptView !== "transcript") return;
    const { evidence } = request;
    if (evidence.kind === "material") {
      document
        .querySelector<HTMLDetailsElement>("#session-materials")
        ?.setAttribute("open", "");
    }
    window.dispatchEvent(
      new CustomEvent("playback-reveal-source", {
        detail: { sessionId: session.id, id: evidence.id },
      }),
    );
    const target = document.getElementById(evidence.id || "");
    target
      ?.closest<HTMLDetailsElement>(".timeline-day")
      ?.setAttribute("open", "");
    target
      ?.closest<HTMLDetailsElement>(".timeline-hour")
      ?.setAttribute("open", "");
    target?.scrollIntoView({ behavior: "smooth", block: "center" });
    setRequest(null);
  }, [request, session?.id, view, transcriptView]);

  return function openSource(evidence: Evidence) {
    if (evidence.url) {
      window.open(evidence.url, "_blank", "noopener,noreferrer");
      return;
    }
    if (!session) return;
    usePlaybackStore.setState({
      view: "transcript",
      transcriptView: "transcript",
    });
    setRequest({ sessionId: session.id, evidence });
  };
}
