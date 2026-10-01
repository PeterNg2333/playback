import type {
  CaptureStatus,
  Chunk,
  Session,
  Transcript,
} from "../../types/api";

// The transcript timeline: a session's audio parts and transcripts grouped by day and
// hour, with quiet audio folded together and settled passages combined for reading.

export type TimelineEntry =
  | { kind: "audio"; chunks: Chunk[]; transcript?: Transcript; at: Date }
  | { kind: "transcript"; transcript: Transcript; at: Date }
  | { kind: "silence"; chunks: Chunk[]; at: Date }
  | {
      kind: "live";
      segment: NonNullable<CaptureStatus["activeSegments"]>[number];
      at: Date;
    };

export function transcriptDays(
  session: Session,
  activeSegments: NonNullable<CaptureStatus["activeSegments"]> = [],
) {
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
      if (
        chunk.status === "silent" ||
        chunk.status === "vad-silence" ||
        chunk.status === "asr-empty"
      ) {
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
  for (const segment of activeSegments) {
    const covered = session.chunks.filter(
      (chunk) =>
        chunk.sourceId === segment.sourceId &&
        chunk.endMs > segment.startMs &&
        chunk.startMs < segment.endMs,
    );
    let completedThrough = segment.startMs;
    for (const chunk of covered
      .filter((chunk) =>
        ["transcribed", "silent", "vad-silence", "asr-empty"].includes(
          chunk.status,
        ),
      )
      .sort((a, b) => a.startMs - b.startMs)) {
      if (chunk.startMs <= completedThrough)
        completedThrough = Math.max(completedThrough, chunk.endMs);
    }
    if (
      completedThrough >= segment.endMs ||
      session.transcripts.some(
        (text) =>
          text.sourceId === segment.sourceId &&
          text.startMs <= segment.startMs &&
          text.endMs >= segment.endMs,
      )
    )
      continue;
    if (segment.interimText) {
      for (let index = entries.length - 1; index >= 0; index--) {
        const entry = entries[index];
        if (
          entry.kind === "audio" &&
          !entry.transcript &&
          entry.chunks.every(
            (chunk) =>
              chunk.sourceId === segment.sourceId &&
              chunk.startMs === segment.startMs,
          )
        )
          entries.splice(index, 1);
      }
    }
    entries.push({ kind: "live", segment, at: new Date(segment.recordedAt) });
  }
  entries.sort((a, b) => a.at.getTime() - b.at.getTime());
  const quietByHour = new Map<
    string,
    Extract<TimelineEntry, { kind: "silence" }>
  >();
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

export type PassageEntry = {
  kind: "passage";
  transcripts: Transcript[];
  at: Date;
};
export type DisplayEntry = TimelineEntry | PassageEntry;

// Display consolidation only: original identity/timing/audio survives. Recent finals stay separate.
export function combineTranscriptEntries(
  entries: TimelineEntry[],
  settledThrough: number,
): DisplayEntry[] {
  const result: DisplayEntry[] = [],
    active = new Map<string, PassageEntry>();
  for (const entry of entries) {
    const text =
      entry.kind === "transcript"
        ? entry.transcript
        : entry.kind === "audio"
          ? entry.transcript
          : undefined;
    if (
      !text ||
      text.endMs > settledThrough ||
      text.uncertain ||
      !text.original ||
      text.recognitionStatus === "asr-empty"
    ) {
      if (entry.kind === "audio" || entry.kind === "silence")
        for (const chunk of entry.chunks) active.delete(chunk.sourceId);
      result.push(entry);
      continue;
    }
    const prior = active.get(text.sourceId),
      last = prior?.transcripts.at(-1);
    const chars =
      prior?.transcripts.reduce(
        (sum, x) => sum + (x.displayOriginal ?? x.original).length,
        0,
      ) ?? 0;
    if (
      prior &&
      last &&
      text.startMs >= last.endMs - 1000 &&
      text.startMs <= last.endMs + 1500 &&
      text.endMs - prior.transcripts[0].startMs <= 120000 &&
      prior.transcripts.length < 16 &&
      chars + (text.displayOriginal ?? text.original).length <= 3200
    )
      prior.transcripts.push(text);
    else {
      const passage: PassageEntry = {
        kind: "passage",
        transcripts: [text],
        at: entry.at,
      };
      active.set(text.sourceId, passage);
      result.push(passage);
    }
  }
  return result.map((entry) =>
    entry.kind === "passage" && entry.transcripts.length === 1
      ? { kind: "transcript", transcript: entry.transcripts[0], at: entry.at }
      : entry,
  );
}
