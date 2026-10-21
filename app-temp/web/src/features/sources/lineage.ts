import type { Session } from "../../lib/backend/schemas";

// A session rerun replaces transcript rows with longer segments. Saved answers and explanations keep the
// IDs they cited; this follows them to the segment that holds the same audio now.
export function currentSourceId(
  session: Session | null | undefined,
  id: string,
) {
  const map = session?.supersessions;
  let current = id;
  for (let step = 0; map && step < 32 && map[current]; step++)
    current = map[current];
  return current;
}

export function findTranscript(
  session: Session | null | undefined,
  id: string,
) {
  const current = currentSourceId(session, id);
  return session?.transcripts.find((item) => item.id === current);
}
