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
    <details className="materials-section" id="session-materials">
      <summary>
        Text materials <span>{session?.materials.length || 0}</span>
      </summary>
      <div className="materials-content">
        <Button
          onClick={() => attachMaterial(session)}
          disabled={!session || !!busy || workspaceLoading}
        >
          Attach text material
        </Button>
        {session?.materials.map((material) => (
          <article className="material-item" id={material.id} key={material.id}>
            <strong>{material.name}</strong>
            <p>{material.text.slice(0, PREVIEW_CHARS)}</p>
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
