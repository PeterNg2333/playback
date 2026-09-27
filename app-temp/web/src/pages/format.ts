import type { Chunk, Session, Transcript } from "../types/api";

export const time = (ms: number) => {
  const seconds = Math.floor(ms / 1000);
  const clock = `${String(Math.floor((seconds % 3600) / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
  return seconds >= 3600
    ? `${String(Math.floor(seconds / 3600)).padStart(2, "0")}:${clock}`
    : clock;
};
export const cleanAsrText = (value: string) =>
  value.replace(/<\|[^|>]*\|>/g, "").trim();
export const chunkStatus = (status: string) =>
  (
    ({
      silent: "Exact digital silence · ASR skipped",
      "asr-empty": "ASR returned no words · audio retained",
      "asr-error": "ASR failed · retrying",
      "awaiting-consent": "Queued for ASR · restart the API",
      "pending-asr": "Queued for ASR",
      transcribing: "Transcribing…",
      transcribed: "Transcript ready",
    }) as Record<string, string>
  )[status] || status;

export type TimelineEntry =
  | { kind: "audio"; chunks: Chunk[]; transcript?: Transcript; at: Date }
  | { kind: "transcript"; transcript: Transcript; at: Date }
  | { kind: "silence"; chunks: Chunk[]; at: Date };

export function transcriptDays(session: Session) {
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
          chunk.sequence === previous.sequence + 1 &&
          chunk.startMs >= previous.startMs &&
          chunk.startMs <= previous.endMs + 1000 &&
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
          chunk.status === previous.status &&
          chunk.sequence === previous.sequence + 1 &&
          chunk.startMs >= previous.startMs &&
          chunk.startMs <= previous.endMs + 1000 &&
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
  entries.sort((a, b) => a.at.getTime() - b.at.getTime());
  for (const entry of entries) {
    const at = entry.at;
    const day = new Intl.DateTimeFormat(undefined, {
      weekday: "long",
      year: "numeric",
      month: "long",
      day: "numeric",
    }).format(at);
    const hour = String(at.getHours()).padStart(2, "0") + ":00";
    if (!days.has(day)) days.set(day, new Map());
    const hours = days.get(day)!;
    if (!hours.has(hour)) hours.set(hour, []);
    hours.get(hour)!.push(entry);
  }
  return days;
}
