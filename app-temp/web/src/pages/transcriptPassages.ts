import type { Transcript } from "../types/api";
import type { TimelineEntry } from "./format";

export type PassageEntry = { kind: "passage"; transcripts: Transcript[]; at: Date };
export type DisplayEntry = TimelineEntry | PassageEntry;

// Display consolidation only: original identity/timing/audio survives. Recent finals stay separate.
export function combineTranscriptEntries(entries: TimelineEntry[], settledThrough: number): DisplayEntry[] {
  const result: DisplayEntry[] = [], active = new Map<string, PassageEntry>();
  for (const entry of entries) {
    const text = entry.kind === "transcript" ? entry.transcript : entry.kind === "audio" ? entry.transcript : undefined;
    if (!text || text.endMs > settledThrough || text.uncertain || !text.original || text.recognitionStatus === "asr-empty") {
      if (entry.kind === "audio" || entry.kind === "silence") for (const chunk of entry.chunks) active.delete(chunk.sourceId);
      result.push(entry); continue;
    }
    const prior = active.get(text.sourceId), last = prior?.transcripts.at(-1);
    const chars = prior?.transcripts.reduce((sum, x) => sum + (x.displayOriginal ?? x.original).length, 0) ?? 0;
    if (prior && last && text.startMs >= last.endMs - 1000 && text.startMs <= last.endMs + 1500 &&
        text.endMs - prior.transcripts[0].startMs <= 120000 && prior.transcripts.length < 16 &&
        chars + (text.displayOriginal ?? text.original).length <= 3200) prior.transcripts.push(text);
    else {
      const passage: PassageEntry = { kind: "passage", transcripts: [text], at: entry.at };
      active.set(text.sourceId, passage); result.push(passage);
    }
  }
  return result.map(entry => entry.kind === "passage" && entry.transcripts.length === 1
    ? { kind: "transcript", transcript: entry.transcripts[0], at: entry.at } : entry);
}
