import type { Session, Transcript } from "../types/api";

export const time = (ms: number) =>
  `${String(Math.floor(ms / 60000)).padStart(2, "0")}:${String(Math.floor((ms % 60000) / 1000)).padStart(2, "0")}`;
export const cleanAsrText = (value: string) =>
  value.replace(/<\|[^|>]*\|>/g, "").trim();
export const chunkStatus = (status: string) =>
  (
    ({
      silent: "Exact digital silence · ASR skipped",
      "asr-empty": "ASR returned no words · audio retained",
      "asr-error": "Recognition failed · retry queued",
      "awaiting-consent":
        "Audio saved · waiting for external processing consent",
      transcribing: "Recognizing speech",
      transcribed: "Transcript ready",
    }) as Record<string, string>
  )[status] || status;

export function transcriptDays(session: Session) {
  const days = new Map<string, Map<string, Transcript[]>>();
  const ordered = session.transcripts
    .map((entry) => ({
      entry,
      at: entry.recordedAt
        ? new Date(entry.recordedAt)
        : new Date(new Date(session.createdAt).getTime() + entry.startMs),
    }))
    .sort((first, second) => first.at.getTime() - second.at.getTime());
  for (const { entry, at } of ordered) {
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
