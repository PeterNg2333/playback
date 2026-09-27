import type { CaptureStatus, Chunk, Session, Transcript } from "../types/api";

export const time = (ms: number) => {
  const seconds = Math.floor(ms / 1000);
  const clock = `${String(Math.floor((seconds % 3600) / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
  return seconds >= 3600
    ? `${String(Math.floor(seconds / 3600)).padStart(2, "0")}:${clock}`
    : clock;
};
export const clockTime = (date: Date) =>
  `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}:${String(date.getSeconds()).padStart(2, "0")}`;
export const recordedRange = (
  recordedAt: string | null | undefined,
  sessionCreatedAt: string | undefined,
  startMs: number,
  endMs: number,
) => {
  const start = recordedAt
    ? new Date(recordedAt)
    : new Date(new Date(sessionCreatedAt || 0).getTime() + startMs);
  const end = new Date(start.getTime() + endMs - startMs);
  return { start: clockTime(start), end: clockTime(end) };
};
export const cleanAsrText = (value: string) =>
  value.replace(/<\|[^|>]*\|>/g, "").trim();
export const chunkStatus = (status: string) =>
  (
    ({
      silent: "Exact digital silence · ASR skipped",
      "asr-empty": "ASR returned no words · audio retained",
      "asr-error": "ASR failed · retrying",
      "asr-manual": "ASR stopped · retry manually",
      "awaiting-consent": "Queued for ASR · restart the API",
      "pending-asr": "Queued for ASR",
      transcribing: "Transcribing…",
      transcribed: "Transcript ready",
    }) as Record<string, string>
  )[status] || status;

export type TimelineEntry =
  | { kind: "audio"; chunks: Chunk[]; transcript?: Transcript; at: Date }
  | { kind: "transcript"; transcript: Transcript; at: Date }
  | { kind: "silence"; chunks: Chunk[]; at: Date }
  | { kind: "live"; segment: NonNullable<CaptureStatus["activeSegments"]>[number]; at: Date };

export function transcriptDays(session: Session, activeSegments: NonNullable<CaptureStatus["activeSegments"]> = []) {
  const days = new Map<string, Map<string, TimelineEntry[]>>();
  const start = new Date(session.createdAt).getTime();
  const dateAt = (recordedAt: string | null | undefined, startMs: number) =>
    recordedAt ? new Date(recordedAt) : new Date(start + startMs);
  const transcripts = new Map(
    session.transcripts.map((entry) => [entry.id, entry]),
  );
  const entries: TimelineEntry[] = [];
  const bySource = new Map<string, Chunk[]>();
  for (const chunk of session.chunks) {
    if (!bySource.has(chunk.sourceId)) bySource.set(chunk.sourceId, []);
    bySource.get(chunk.sourceId)!.push(chunk);
  }
  for (const chunks of bySource.values()) {
    let silence: Extract<TimelineEntry, { kind: "silence" }> | null = null;
    let audio: Extract<TimelineEntry, { kind: "audio" }> | null = null;
    for (const chunk of [...chunks].sort((a, b) => a.sequence - b.sequence)) {
      const at = dateAt(chunk.recordedAt, chunk.startMs);
      const transcript = transcripts.get(chunk.id);
      if (chunk.status === "silent" || chunk.status === "asr-empty") {
        audio = null;
        transcripts.delete(chunk.id);
        const previous = silence?.chunks.at(-1);
        if (
          previous &&
          chunk.startMs >= previous.endMs - 1000 &&
          chunk.startMs <= previous.endMs + 1000 &&
          silence!.chunks.length < 100 &&
          at.getHours() === silence!.at.getHours() &&
          at.toDateString() === silence!.at.toDateString()
        ) {
          silence!.chunks.push(chunk);
        } else {
          silence = { kind: "silence", chunks: [chunk], at };
          entries.push(silence);
        }
      } else {
        silence = null;
        const previous = audio?.chunks.at(-1);
        if (
          !transcript &&
          audio &&
          !audio.transcript &&
          previous &&
          (chunk.status === previous.status ||
            (["asr-error", "asr-manual"].includes(chunk.status) &&
              ["asr-error", "asr-manual"].includes(previous.status))) &&
          chunk.startMs >= previous.endMs - 1000 &&
          chunk.startMs <= previous.endMs + 1000 &&
          audio.chunks.length < 100 &&
          at.getHours() === audio.at.getHours() &&
          at.toDateString() === audio.at.toDateString()
        ) {
          audio.chunks.push(chunk);
        } else {
          audio = { kind: "audio", chunks: [chunk], transcript, at };
          entries.push(audio);
        }
      }
      transcripts.delete(chunk.id);
    }
  }
  for (const transcript of transcripts.values())
    entries.push({
      kind: "transcript",
      transcript,
      at: dateAt(transcript.recordedAt, transcript.startMs),
    });
  for (const segment of activeSegments)
    entries.push({ kind: "live", segment, at: new Date(segment.recordedAt) });
  entries.sort((a, b) => a.at.getTime() - b.at.getTime());
  const quietByHour = new Map<string, Extract<TimelineEntry, { kind: "silence" }>>();
  for (const entry of entries) {
    const at = entry.at;
    const day = `${at.getFullYear()}-${String(at.getMonth() + 1).padStart(2, "0")}-${String(at.getDate()).padStart(2, "0")}`;
    const hour = String(at.getHours()).padStart(2, "0") + ":00";
    if (!days.has(day)) days.set(day, new Map());
    const hours = days.get(day)!;
    if (!hours.has(hour)) hours.set(hour, []);
    if (entry.kind === "silence") {
      const key = `${day}/${hour}`;
      const previous = quietByHour.get(key);
      if (previous) previous.chunks.push(...entry.chunks);
      else {
        quietByHour.set(key, entry);
        hours.get(hour)!.push(entry);
      }
    } else hours.get(hour)!.push(entry);
  }
  return days;
}
