import type { Session } from "../types/api";
import {
  SessionSchema,
  TranscriptSchema,
  ChunkSchema,
  MaterialSchema,
  TermCandidateSchema,
  TermInsightSchema,
} from "../types/api";
import { api } from "./api";

type Change = { kind: string; value: Record<string, unknown> | string };
type Delta = {
  cursor: string;
  reset: boolean;
  hasMore: boolean;
  changes: Change[];
};
const fields: Record<
  string,
  "transcripts" | "chunks" | "materials" | "termInsights" | "terms"
> = {
  transcript: "transcripts",
  chunk: "chunks",
  material: "materials",
  insight: "termInsights",
  term: "terms",
};
const schemas = {
  transcript: TranscriptSchema,
  chunk: ChunkSchema,
  material: MaterialSchema,
  insight: TermInsightSchema,
  term: TermCandidateSchema,
};
function merge(session: Session | null, delta: Delta): Session | null {
  const next = session && !delta.reset ? { ...session } : null;
  let draft: Record<string, unknown> | null = next;
  const mutable = new Map<
    string,
    { list: Record<string, unknown>[]; index: Map<unknown, number> }
  >();
  for (const change of delta.changes) {
    if (change.kind === "meta") {
      const meta = SessionSchema.parse({
        transcripts: [],
        chunks: [],
        materials: [],
        terms: [],
        termInsights: [],
        ...(change.value as object),
      });
      draft = {
        ...meta,
        transcripts: draft?.transcripts ?? [],
        chunks: draft?.chunks ?? [],
        materials: draft?.materials ?? [],
        terms: draft?.terms ?? [],
        termInsights: draft?.termInsights ?? [],
      };
    } else if (draft && change.kind === "removed") {
      const [kind, ...rest] = String(change.value).split(":");
      const field = fields[kind];
      const id = rest.join(":");
      if (field) {
        draft[field] = (draft[field] as Record<string, unknown>[]).filter(
          (x) => String(field === "terms" ? x.text : x.id) !== id,
        );
        mutable.delete(field);
      }
    } else if (draft && fields[change.kind]) {
      const field = fields[change.kind];
      const value = schemas[change.kind as keyof typeof schemas].parse(
        change.value,
      ) as Record<string, unknown>;
      const idField = field === "terms" ? "text" : "id";
      if (!mutable.has(field)) {
        const list = (draft[field] as Record<string, unknown>[]).slice();
        mutable.set(field, {
          list,
          index: new Map(list.map((x, i) => [x[idField], i])),
        });
        draft[field] = list;
      }
      const bucket = mutable.get(field)!,
        index = bucket.index.get(value[idField]);
      if (index === undefined) {
        bucket.index.set(value[idField], bucket.list.length);
        bucket.list.push(value);
      } else bucket.list[index] = value;
    }
  }
  if (!draft) return null;
  return draft as Session;
}
export async function readSession(
  id: string,
  previous: Session | null,
  cursor: string | undefined,
  signal: AbortSignal,
) {
  let current = previous;
  const fragments = new Map<
    string,
    { kind: string; count: number; pieces: Map<number, string>; size: number }
  >();
  for (let page = 0; page < 200; page++) {
    const delta = await api<Delta>(
      `/sessions/${id}/sync${cursor ? "?cursor=" + cursor : ""}`,
      "GET",
      undefined,
      undefined,
      signal,
    );
    if (delta.reset) fragments.clear();
    const changes: Change[] = [];
    for (const change of delta.changes) {
      if (change.kind !== "fragment") {
        changes.push(change);
        continue;
      }
      const value = change.value as {
        kind: string;
        key: string;
        index: number;
        count: number;
        text: string;
      };
      if (
        !Number.isInteger(value.index) ||
        value.index < 0 ||
        value.count > 334 ||
        value.count < 1 ||
        value.index >= value.count ||
        value.text.length > 24000
      )
        throw new Error("Invalid session fragment");
      if (!fragments.has(value.key))
        fragments.set(value.key, {
          kind: value.kind,
          count: value.count,
          pieces: new Map(),
          size: 0,
        });
      const item = fragments.get(value.key)!;
      if (item.kind !== value.kind || item.count !== value.count)
        throw new Error("Session fragments changed during assembly");
      if (!item.pieces.has(value.index)) {
        item.pieces.set(value.index, value.text);
        item.size += value.text.length;
      }
      if (item.size > 8000000 || fragments.size > 8)
        throw new Error("Session fragment budget exceeded");
      if (item.pieces.size === item.count) {
        changes.push({
          kind: item.kind,
          value: JSON.parse(
            Array.from({ length: item.count }, (_, i) =>
              item.pieces.get(i),
            ).join(""),
          ),
        });
        fragments.delete(value.key);
      }
    }
    if (changes.length || delta.reset)
      current = merge(current, { ...delta, changes });
    cursor = delta.cursor;
    if (!delta.hasMore) {
      if (!current) throw new Error("Session sync returned no session");
      if (fragments.size)
        throw new Error("Session sync ended before a record was complete");
      return { session: current, cursor };
    }
  }
  throw new Error("Session sync exceeded its page limit");
}
