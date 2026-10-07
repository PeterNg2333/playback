// Pure offline characterization of the actual timeline function, without a browser or API.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("../src/features/transcript/timelineEntries.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ES2022, target: ts.ScriptTarget.ES2022 } }).outputText;
const { transcriptDays } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);
const chunk = (sequence, status) => ({ id: `part-${sequence}`, sourceId: "microphone", sequence, status,
  startMs: sequence * 8000, endMs: (sequence + 1) * 8000 });
const chunks = [chunk(0, "vad-silence"), chunk(1, "transcribed"), chunk(2, "vad-silence"), chunk(3, "asr-empty")];
const session = { createdAt: "2026-10-07T01:00:00Z", chunks, transcripts: [
  { id: "part-1", sourceId: "microphone", startMs: 8000, endMs: 16000, original: "Intervening speech" },
] };
const entries = [...transcriptDays(session).values()].flatMap((hours) => [...hours.values()].flat());
assert.deepEqual(entries.map((entry) => entry.kind), ["silence", "audio"]);
assert.deepEqual(entries[0].chunks.map((part) => part.id), ["part-0", "part-2", "part-3"]);
assert.equal(entries[1].transcript.id, "part-1");
assert.equal(chunks.length, 4);
console.log("Refactor audio baseline passed: quiet-speech-quiet/asr-empty fold at first hourly row; original parts remain (Phase 3 fix pending)");
