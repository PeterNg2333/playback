import { DurableQueue } from "./dist/queue.js";
import { mkdtempSync, rmSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import assert from "node:assert/strict";

const root = mkdtempSync(path.join(tmpdir(), "playback-queue-"));
try {
  const session = "a".repeat(32);
  let queue = new DurableQueue(root);
  const silentTenSeconds = Array(10_000).fill(0);
  for (let index = 0; index < 1080; index++) {
    const chunk = queue.save(
      session,
      "system",
      index * 10_000,
      (index + 1) * 10_000,
      silentTenSeconds,
      1000,
      false,
    );
    assert.equal(chunk.sequence, index);
    assert.equal(chunk.endMs - chunk.startMs, 10_000);
  }
  assert.equal(queue.pending.size, 1080);
  queue = new DurableQueue(root);
  assert.equal(queue.pending.size, 1080);
  const next = queue.save(
    session,
    "system",
    10_800_000,
    10_810_000,
    silentTenSeconds,
    1000,
    false,
  );
  assert.equal(next.sequence, 1080);
  const microphone = queue.save(
    session,
    "microphone",
    0,
    10_000,
    silentTenSeconds,
    1000,
    false,
  );
  assert.equal(microphone.sequence, 0);
  assert.equal(readFileSync(microphone.file).toString("ascii", 0, 4), "RIFF");
  assert.notEqual(next.id, microphone.id);
  const damaged = readFileSync(microphone.file);
  damaged[44] = 1;
  writeFileSync(microphone.file, damaged);
  const checked = new DurableQueue(root);
  assert.match(checked.error, /hash mismatch/);
  assert.ok(!checked.pending.has(microphone.id));
  console.log(
    "Queue check passed: 3-hour synthetic timeline, 1080 recovered system chunks, independent source sequences, valid WAV and corruption detection",
  );
} finally {
  rmSync(root, { recursive: true, force: true });
}
