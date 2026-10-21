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
assert.deepEqual(entries.map((entry) => entry.kind), ["silence", "audio", "silence", "audio"]);
assert.deepEqual(entries[0].chunks.map((part) => part.id), ["part-0"]);
assert.deepEqual(entries[2].chunks.map((part) => part.id), ["part-2"]);
assert.equal(entries[3].chunks[0].status, "asr-empty");
assert.equal(entries[1].transcript.id, "part-1");
assert.equal(chunks.length, 4);
console.log("Quiet audio checks passed: chronological quiet-speech-quiet, ASR-empty remains distinct, originals retained");
const playerSource = await readFile(new URL("../src/features/player/useAudioPlayer.ts", import.meta.url), "utf8");
const playerCompiled = ts.transpileModule(playerSource, { compilerOptions: { module: ts.ModuleKind.ES2022, target: ts.ScriptTarget.ES2022 } }).outputText.replace(/^import[\s\S]*?;/gm, "");
const { rowParts } = await import(`data:text/javascript;base64,${Buffer.from(playerCompiled).toString("base64")}`);
const adjacent = [chunk(0, "silent"), chunk(1, "vad-silence")];
const combinedSession = { id: "session", createdAt: session.createdAt, chunks: adjacent, transcripts: [], combinedAudioSegments: [{ id: "combined", children: adjacent.map(x => ({chunkId: x.id})), startMs: 0, endMs: 16000 }] };
const quiet = [...transcriptDays(combinedSession).values()].flatMap(hours => [...hours.values()].flat());
assert.equal(quiet.length, 1);
assert.equal(rowParts(combinedSession, adjacent).length, 1);
assert.equal(rowParts(combinedSession, adjacent)[0].url, "/api/sessions/session/audio/combined/combined");
assert.equal(rowParts(combinedSession, [adjacent[1]])[0].url, "/api/chunks/part-1/audio");
assert.equal(rowParts(combinedSession, [adjacent[1]])[0].startMs, 8000);
assert.equal([...transcriptDays({...combinedSession, chunks: [adjacent[0], {...adjacent[1], streamId: "resume"}]}).values()].flatMap(hours => [...hours.values()].flat()).length, 2);
console.log("Quiet playback checks passed: one manifest URL for a run, original child citation URL/time and pause splitting");
