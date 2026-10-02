import { useEffect, useState } from "react";
import { usePlaybackStore } from "../../lib/store";
import type { Evidence, Session } from "../../lib/backend/schemas";

// Asks the transcript to show one passage, even one its virtual list has not mounted.
// The browser checks dispatch this event directly, so it stays a window event.
export const REVEAL_SOURCE_EVENT = "playback-reveal-source";
export type RevealSourceDetail = { sessionId: string; id: string };

// Opens a cited source: a web page in a new tab, or the transcript passage or
// teaching material it points to, once the transcript view is showing.
export function useRevealSource(session: Session | null) {
  const [request, setRequest] = useState<{
    sessionId: string;
    evidence: Evidence;
  } | null>(null);
  const view = usePlaybackStore((state) => state.view);

  useEffect(() => {
    if (!request) return;
    if (request.sessionId !== session?.id) {
      setRequest(null);
      return;
    }
    if (view !== "transcript") return;
    const { evidence } = request;
    if (evidence.kind === "material") {
      document
        .querySelector<HTMLDetailsElement>("#session-materials")
        ?.setAttribute("open", "");
    }
    window.dispatchEvent(
      new CustomEvent<RevealSourceDetail>(REVEAL_SOURCE_EVENT, {
        detail: { sessionId: session.id, id: evidence.id ?? "" },
      }),
    );
    document
      .getElementById(evidence.id || "")
      ?.scrollIntoView({ behavior: "smooth", block: "center" });
    setRequest(null);
  }, [request, session?.id, view]);

  return function openSource(evidence: Evidence) {
    if (evidence.url) {
      window.open(evidence.url, "_blank", "noopener,noreferrer");
      return;
    }
    if (!session) return;
    usePlaybackStore.setState({ view: "transcript" });
    setRequest({ sessionId: session.id, evidence });
  };
}
