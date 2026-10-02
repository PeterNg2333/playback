import type { Chunk } from "../../lib/backend/schemas";

// How speech recognition reads in the transcript: each audio part's status, the
// summary beside the Transcript heading, and provider text without control tokens.

export const cleanAsrText = (value: string) =>
  value.replace(/<\|[^|>]*\|>/g, "").trim();

export const chunkStatus = (status: string) =>
  (
    ({
      silent: "Exact digital silence · ASR skipped",
      "vad-silence": "VAD found no speech · audio retained",
      "asr-empty": "ASR returned no words · audio retained",
      "asr-error": "ASR failed · retrying",
      "asr-manual": "ASR stopped · retry manually",
      "awaiting-consent": "Queued for ASR · restart the API",
      "pending-asr": "Queued for ASR",
      transcribing: "Transcribing…",
      transcribed: "Transcript ready",
    }) as Record<string, string>
  )[status] || status;

// Show the most actionable ASR state when a session has several pending parts.
export function asrSummary(chunks: Chunk[], paused: boolean) {
  if (paused) return "ASR paused · audio saved locally";
  const statuses = new Set(chunks.map((chunk) => chunk.status));
  if (statuses.has("awaiting-consent")) return "ASR waiting · restart API";
  if (statuses.has("asr-manual")) return "ASR stopped · manual retry available";
  if (statuses.has("asr-error")) return "ASR failed · retrying";
  if (statuses.has("transcribing")) return "Transcribing…";
  if (statuses.has("pending-asr")) return "ASR queued";
  return null;
}
