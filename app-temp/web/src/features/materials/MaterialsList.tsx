import { usePlaybackStore } from "../../lib/store";
import type { Session } from "../../lib/backend/schemas";
import { askAboutTerm } from "../ask/askAbout";
import { Button } from "../../components/Button";
import { Tag } from "../../components/Tag";
import { attachMaterial } from "./attachMaterial";

const PREVIEW_CHARS = 180;

// The session's text materials: attach another file, read the opening, ask about its key terms.
export function MaterialsList({ session }: { session: Session | null }) {
  const busy = usePlaybackStore((state) => state.busy);
  const workspaceLoading = usePlaybackStore((state) => state.workspaceLoading);
  return (
    <details className="border-b border-line" id="session-materials">
      <summary className="flex cursor-pointer items-center gap-2 px-5 py-3 text-[11px] font-bold text-muted">
        Text materials{" "}
        <span className="rounded-full bg-accent-soft px-1.5 py-px text-accent">
          {session?.materials.length || 0}
        </span>
      </summary>
      <div className="px-5 pb-3">
        <Button
          onClick={() => attachMaterial(session)}
          disabled={!session || !!busy || workspaceLoading}
        >
          Attach text material
        </Button>
        {session?.materials.map((material) => (
          <article
            className="scroll-mt-5 border-b border-line py-3 last:border-b-0"
            id={material.id}
            key={material.id}
          >
            <strong className="text-[12px]">{material.name}</strong>
            <p className="mt-0.75 text-[11px] text-muted">
              {material.text.slice(0, PREVIEW_CHARS)}
            </p>
            {session.terms
              .filter((candidate) =>
                candidate.materialIds.includes(material.id),
              )
              .map((candidate) => (
                <Tag
                  key={candidate.text}
                  onClick={() => askAboutTerm(session, candidate)}
                >
                  {candidate.text}
                </Tag>
              ))}
          </article>
        ))}
      </div>
    </details>
  );
}
