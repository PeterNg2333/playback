import assert from "node:assert/strict";
import { createHash } from "node:crypto";

const base =
  process.env.PLAYBACK_OFFLINE_TEST === "yes"
    ? "http://127.0.0.1:5079/api"
    : "http://127.0.0.1:5078/api";
async function call(path, method = "GET", body) {
  const response = await fetch(base + path, {
    method,
    headers: body ? { "Content-Type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
  });
  return { status: response.status, data: await response.json() };
}
const health = (await call("/health")).data;
if (!health.mongo)
  throw new Error("Local MongoDB must be running for this integration check");
const created = [];
let groupId;
try {
  const group = (await call("/groups", "POST", { name: "E2E group synthetic" }))
    .data;
  groupId = group.id;
  const first = (
    await call("/sessions", "POST", { title: "E2E demo synthetic A", groupId })
  ).data;
  created.push(first.id);
  const second = (
    await call("/sessions", "POST", { title: "E2E demo synthetic B" })
  ).data;
  created.push(second.id);
  assert.equal(
    (
      await call("/capture/start", "POST", {
        sessionId: first.id,
        sourceMode: "unknown",
      })
    ).status,
    409,
  );
  assert.equal(
    (await call("/capture/status")).data.state,
    "idle",
    "Invalid recording mode must not open an audio device",
  );
  assert.equal((await call(`/sessions/${first.id}`)).data.groupId, groupId);
  await call(`/sessions/${second.id}/group`, "PUT", { groupId });
  assert.equal((await call(`/sessions/${second.id}`)).data.groupId, groupId);
  await call(`/sessions/${second.id}/group`, "PUT", { groupId: null });
  assert.equal((await call(`/sessions/${second.id}`)).data.groupId, null);
  await call(`/groups/${groupId}`, "PUT", { name: "E2E group renamed" });
  await call(`/sessions/${first.id}/materials`, "POST", {
    name: "A.txt",
    text: "A-only synthetic source",
  });
  await call(`/sessions/${second.id}/materials`, "POST", {
    name: "B.txt",
    text: "B-only synthetic source",
  });
  const one = (await call(`/sessions/${first.id}`)).data;
  assert.equal(one.asrLanguage, "auto");
  assert.equal(one.noteLanguage, "zh-Hant");
  for (const [asrLanguage, noteLanguage] of [["yue", "zh-Hant"], ["zh", "zh-Hans"], ["en", "en"]]) {
    const updated = await call(`/sessions/${first.id}/languages`, "PUT", { asrLanguage, noteLanguage });
    assert.equal(updated.status, 200);
    const saved = (await call(`/sessions/${first.id}`)).data;
    assert.equal(saved.asrLanguage, asrLanguage);
    assert.equal(saved.noteLanguage, noteLanguage);
  }
  assert.equal((await call(`/sessions/${second.id}`)).data.asrLanguage, "auto");
  assert.equal((await call(`/sessions/${first.id}/languages`, "PUT", { asrLanguage: "zh-Hant", noteLanguage: "en" })).status, 409);
  assert.equal((await call(`/sessions/${first.id}/languages`, "PUT", { asrLanguage: "yue", noteLanguage: "ja" })).status, 409);
  assert.equal((await call(`/sessions/${first.id}/languages`, "PUT", { asrLanguage: "yue-en", noteLanguage: "en", asrModel: "openai/whisper-large-v3-turbo" })).status, 200);
  assert.equal((await call(`/sessions/${first.id}`)).data.asrModel, "openai/whisper-large-v3-turbo");
  assert.equal((await call(`/sessions/${first.id}/languages`, "PUT", { asrLanguage: "yue-en", noteLanguage: "en", asrModel: "invalid/model" })).status, 409);
  assert.equal((await call(`/sessions/${second.id}`)).data.asrModel, null);
  for (const language of ["yue-Hant", "zh-Hans", "en", "zh-Hant"]) {
    assert.equal((await call(`/sessions/${first.id}/translation`, "PUT", { enabled: false, language })).status, 200);
    assert.equal((await call(`/sessions/${first.id}`)).data.translationLanguage, language);
  }
  assert.deepEqual(
    one.materials.map((x) => x.name),
    ["A.txt"],
  );
  const note1 = (
    await call(`/sessions/${first.id}/notes`, "POST", { markdown: "# First" })
  ).data;
  const note2 = (
    await call(`/sessions/${first.id}/notes`, "POST", { markdown: "# Second" })
  ).data;
  assert.equal(note1.version, 1);
  assert.equal(note2.version, 2);
  const versions = (await call(`/sessions/${first.id}/notes`)).data;
  assert.deepEqual(
    versions.map((x) => x.markdown),
    ["# First", "# Second"],
  );
  assert.match(versions[1].inputHash, /^[a-f0-9]{64}$/);
  assert.equal(versions[1].basedOnVersion, 1);
  assert.equal((await call(`/sessions/${second.id}`)).data.noteVersion, 0);
  const restored = (await call(`/sessions/${first.id}/notes/1/restore`, "POST")).data;
  assert.equal(restored.version, 3);
  assert.equal((await call(`/sessions/${first.id}`)).data.noteMarkdown, "# First");
  await call(`/sessions/${first.id}/notes/2/restore`, "POST");
  assert.equal((await call(`/sessions/${first.id}/notes`)).data.length, 4, "Restore preserves every earlier version");

  const wav = Buffer.alloc(44 + 16000 * 2);
  wav.write("RIFF", 0);
  wav.writeUInt32LE(wav.length - 8, 4);
  wav.write("WAVEfmt ", 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(16000, 24);
  wav.writeUInt32LE(32000, 28);
  wav.writeUInt16LE(2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write("data", 36);
  wav.writeUInt32LE(32000, 40);
  const hash = createHash("sha256").update(wav).digest("hex");
  async function upload(
    sessionId,
    sourceId,
    sequence,
    startMs,
    endMs,
    bytes,
    sha256,
  ) {
    const form = new FormData();
    for (const [key, value] of Object.entries({
      sessionId,
      sourceId,
      sequence,
      startMs,
      endMs,
      sha256,
    }))
      form.set(key, String(value));
    form.set("file", new Blob([bytes], { type: "audio/wav" }), "synthetic.wav");
    const response = await fetch(base + "/chunks", {
      method: "POST",
      body: form,
    });
    return { status: response.status, data: await response.json() };
  }
  async function waitForChunk(sessionId, chunkId, status) {
    for (let attempt = 0; attempt < 50; attempt++) {
      const session = (await call(`/sessions/${sessionId}`)).data;
      if (session.chunks.find((x) => x.id === chunkId)?.status === status)
        return;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(`Chunk ${chunkId} never reached ${status}`);
  }
  async function waitForAttempt(sessionId, chunkId, attempts, status) {
    for (let attempt = 0; attempt < 50; attempt++) {
      const session = (await call(`/sessions/${sessionId}`)).data;
      const chunk = session.chunks.find((x) => x.id === chunkId);
      if (chunk?.asrAttempts === attempts && chunk.status === status) return;
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    throw new Error(
      `Chunk ${chunkId} never reached ASR attempt ${attempts} / ${status}`,
    );
  }
  const sent = await upload(first.id, "system", 0, 0, 1000, wav, hash);
  assert.equal(sent.status, 202);
  assert.equal(sent.data.status, "pending-asr");
  const replay = await upload(first.id, "system", 0, 0, 1000, wav, hash);
  assert.equal(replay.data.id, sent.data.id);
  const secondSource = await upload(
    first.id,
    "microphone",
    0,
    100,
    1100,
    wav,
    hash,
  );
  assert.notEqual(secondSource.data.id, sent.data.id);
  const secondSession = await upload(
    second.id,
    "system",
    0,
    0,
    1000,
    wav,
    hash,
  );
  assert.notEqual(secondSession.data.id, sent.data.id);
  const range = (await call(`/sessions/${first.id}`)).data.chunks;
  assert.deepEqual(
    range.map((x) => [x.sourceId, x.startMs, x.endMs]),
    [
      ["system", 0, 1000],
      ["microphone", 100, 1100],
    ],
  );
  assert.equal((await call(`/sessions/${second.id}`)).data.chunks.length, 1);
  if (process.env.PLAYBACK_OFFLINE_TEST === "yes") {
    async function expectSafeChunk(sessionId, chunkId) {
      if (process.env.PLAYBACK_PAUSE_EXTERNAL_ASR === "yes") {
        assert.equal(
          (await call(`/sessions/${sessionId}`)).data.chunks.find(
            (x) => x.id === chunkId,
          )?.status,
          "pending-asr",
        );
      } else {
        await waitForChunk(sessionId, chunkId, "silent");
      }
    }
    const fixture = await call(
      `/testing/sessions/${first.id}/transcripts`,
      "POST",
      {
        chunkId: sent.data.id,
        text: "Synthetic lecture uses Fourier Transform and FFT bins.",
      },
    );
    assert.equal(fixture.status, 200);
    await call(`/sessions/${first.id}/translation`, "PUT", {
      enabled: true,
      language: "zh-Hant",
    });
    const translated = (await call(`/sessions/${first.id}`)).data;
    assert.equal(translated.translationEnabled, true);
    assert.equal(
      translated.transcripts[0].translationStatus,
      "pending",
      "Existing transcript must be queued for translation",
    );
    assert.deepEqual(
      (await call(`/testing/sessions/${first.id}/translations/pending`)).data,
      [sent.data.id],
    );
    const lateChunk = await upload(first.id, "late", 0, 1200, 2200, wav, hash);
    await expectSafeChunk(first.id, lateChunk.data.id);
    await call(`/testing/sessions/${first.id}/transcripts`, "POST", {
      chunkId: lateChunk.data.id,
      text: "Synthetic FFT appears after settings were enabled.",
    });
    const emptyChunk = await upload(
      first.id,
      "empty",
      0,
      2300,
      3300,
      wav,
      hash,
    );
    await expectSafeChunk(first.id, emptyChunk.data.id);
    await call(`/testing/sessions/${first.id}/transcripts`, "POST", {
      chunkId: emptyChunk.data.id,
      text: "",
    });
    const emptySession = (await call(`/sessions/${first.id}`)).data;
    assert.equal(
      emptySession.chunks.find((x) => x.id === emptyChunk.data.id).status,
      "asr-empty",
    );
    assert.equal(
      emptySession.transcripts.find((x) => x.id === emptyChunk.data.id)
        .uncertain,
      false,
    );
    assert.equal(
      emptySession.transcripts.find((x) => x.id === emptyChunk.data.id)
        .noteStatus,
      "skipped",
    );
    if (process.env.PLAYBACK_PAUSE_EXTERNAL_ASR !== "yes") {
      const retriedEmpty = await call(
        `/sessions/${first.id}/chunks/retry`,
        "POST",
        { chunkIds: [emptyChunk.data.id] },
      );
      assert.equal(retriedEmpty.status, 202);
      await waitForChunk(first.id, emptyChunk.data.id, "silent");
      assert.equal(
        (await call(`/sessions/${first.id}`)).data.transcripts.some(
          (x) => x.id === emptyChunk.data.id,
        ),
        false,
      );
      const speech = Buffer.from(wav);
      for (let offset = 44; offset < speech.length; offset += 2)
        speech.writeInt16LE(2500, offset);
      // DC/background is correctly rejected by VAD now. Use an unsupported codec
      // to exercise durable processing failure/retry without contacting a provider.
      speech.writeUInt16LE(65535, 20);
      const failed = await upload(
        first.id,
        "failure",
        0,
        45000,
        46000,
        speech,
        createHash("sha256").update(speech).digest("hex"),
      );
      await waitForAttempt(first.id, failed.data.id, 1, "asr-error");
      for (const attempts of [2, 3]) {
        assert.equal(
          (
            await call(`/sessions/${first.id}/chunks/retry`, "POST", {
              chunkIds: [failed.data.id],
            })
          ).status,
          202,
        );
        await waitForAttempt(
          first.id,
          failed.data.id,
          attempts,
          attempts === 3 ? "asr-manual" : "asr-error",
        );
      }
      const duplicate = await upload(
        first.id,
        "failure",
        0,
        45000,
        46000,
        speech,
        createHash("sha256").update(speech).digest("hex"),
      );
      assert.equal(duplicate.status, 200);
      assert.equal(duplicate.data.status, "asr-manual");
      assert.equal(
        (
          await call(`/sessions/${first.id}/chunks/retry`, "POST", {
            chunkIds: [failed.data.id],
          })
        ).status,
        202,
      );
      await waitForAttempt(first.id, failed.data.id, 1, "asr-error");
    }
    const left = Buffer.from(wav);
    const right = Buffer.from(wav);
    for (let offset = 44; offset < wav.length; offset += 2) {
      left.writeInt16LE(8192, offset);
      right.writeInt16LE(16384, offset);
    }
    await upload(
      first.id,
      "mixleft",
      0,
      6000,
      7000,
      left,
      createHash("sha256").update(left).digest("hex"),
    );
    await upload(
      first.id,
      "mixright",
      0,
      6000,
      7000,
      right,
      createHash("sha256").update(right).digest("hex"),
    );
    async function playbackSample(source) {
      const url = `${base}/sessions/${first.id}/audio/segments/0${source ? `?source=${source}` : ""}`;
      const response = await fetch(url);
      assert.equal(response.status, 200);
      const bytes = Buffer.from(await response.arrayBuffer());
      const dataAt = bytes.indexOf("data") + 8;
      assert.ok(dataAt >= 8);
      return bytes.readInt16LE(dataAt + 6000 * 16 * 2);
    }
    assert.ok(Math.abs((await playbackSample("mixleft")) - 8192) < 250);
    assert.ok(Math.abs((await playbackSample("mixright")) - 16384) < 250);
    assert.ok(Math.abs((await playbackSample(null)) - 12288) < 250);
    const reviewChunk = await upload(
      first.id,
      "review",
      0,
      3400,
      4400,
      wav,
      hash,
    );
    await expectSafeChunk(first.id, reviewChunk.data.id);
    await call(`/testing/sessions/${first.id}/transcripts`, "POST", {
      chunkId: reviewChunk.data.id,
      text: "Synthetic speech was [unclear].",
    });
    assert.equal(
      (await call(`/sessions/${first.id}`)).data.transcripts.find(
        (x) => x.id === reviewChunk.data.id,
      ).recognitionStatus,
      "review-needed",
    );
    const pendingTranslations = (
      await call(`/testing/sessions/${first.id}/translations/pending`)
    ).data;
    assert.deepEqual(
      new Set(pendingTranslations),
      new Set([sent.data.id, lateChunk.data.id, reviewChunk.data.id]),
      "Existing and new nonempty transcripts must be queued",
    );
    assert.ok(
      translated.terms.some((term) => term.text === "Fourier Transform"),
    );
    const firstFailure = await call(
      `/sessions/${first.id}/notes/generate`,
      "POST",
    );
    assert.equal(firstFailure.status, 409);
    let failed = (await call(`/sessions/${first.id}`)).data.transcripts.find(
      (x) => x.id === sent.data.id,
    );
    assert.equal(failed.noteStatus, "failed");
    assert.equal(failed.noteAttempts, 1);
    const retried = await call(`/sessions/${first.id}/notes/generate`, "POST");
    assert.equal(retried.status, 409);
    failed = (await call(`/sessions/${first.id}`)).data.transcripts.find(
      (x) => x.id === sent.data.id,
    );
    assert.equal(failed.noteAttempts, 2);
    assert.equal(
      (await call(`/sessions/${first.id}`)).data.noteMarkdown,
      "# Second",
      "Failed AI must preserve the user note",
    );
    await call(`/sessions/${first.id}/translation`, "PUT", {
      enabled: false,
      language: "zh-Hant",
    });
    assert.equal(
      (await call(`/sessions/${first.id}`)).data.translationEnabled,
      false,
    );
  }
  console.log(
    `Integration check passed: source isolation, note versions, ${process.env.PLAYBACK_PAUSE_EXTERNAL_ASR === "yes" ? "paused ASR queue" : "bounded ASR retries and manual recovery"}, time ranges`,
  );
} finally {
  for (const id of created) {
    const response = await fetch(`${base}/testing/sessions/${id}`, {
      method: "DELETE",
    });
    assert.equal(response.status, 204, `Test session cleanup failed for ${id}`);
  }
  if (groupId) {
    const response = await fetch(`${base}/testing/groups/${groupId}`, {
      method: "DELETE",
    });
    assert.equal(
      response.status,
      204,
      `Test group cleanup failed for ${groupId}`,
    );
  }
}
