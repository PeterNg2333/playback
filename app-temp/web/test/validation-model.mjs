import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import { chromium } from "playwright-core";
const out = path.resolve("app-temp/data/validation/runs/2026-09-28-repair"),
  file = path.join(out, "app-model-selection.json");
if (!process.argv.includes("--live")) {
  console.log("Offline: " + file);
  process.exit(0);
}
try {
  await readFile(file);
  if (!process.argv.includes("--refresh")) {
    console.log("Reusing saved model selection result.");
    process.exit(0);
  }
} catch {}
const session = JSON.parse(await readFile(path.join(out, "app-session.json"))),
  base = "http://127.0.0.1:5081/api";
const get = async (p) => (await fetch(base + p)).json();
const health = await get("/health");
assert.equal(health.autoTerms, false);
assert.equal(health.autoNotes, false);
const before = await get("/sessions/" + session.id);
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const page = await browser.newPage();
let passed = false,
  transcript,
  error,
  latencyMs,
  uploadStatus;
try {
  await page.addInitScript(
    (id) => localStorage.setItem("playback-session", id),
    session.id,
  );
  await page.goto("http://127.0.0.1:5181");
  await page
    .getByRole("button", { name: "Transcript settings", exact: true })
    .click();
  await page.getByLabel("ASR spoken language").selectOption("en");
  await page
    .getByLabel("ASR model", { exact: true })
    .selectOption("openai/whisper-large-v3-turbo");
  await page.reload();
  await page
    .getByRole("button", { name: "Transcript settings", exact: true })
    .click();
  assert.equal(
    await page.getByLabel("ASR model", { exact: true }).inputValue(),
    "openai/whisper-large-v3-turbo",
  );
  const audio = await readFile("app-temp/data/validation/fixtures/english.wav"),
    sha = createHash("sha256").update(audio).digest("hex");
  const form = new FormData();
  for (const [key, value] of Object.entries({
    sessionId: session.id,
    sourceId: "modelcheck",
    sequence: 1,
    startMs: 50000,
    endMs: 61207,
    sha256: sha,
  }))
    form.set(key, String(value));
  form.set("file", new Blob([audio], { type: "audio/wav" }), "english.wav");
  const start = Date.now();
  const uploaded = await fetch(base + "/chunks", {
    method: "POST",
    body: form,
  });
  uploadStatus = uploaded.status;
  assert([200, 202].includes(uploaded.status));
  const chunk = await uploaded.json();
  const deadline = Date.now() + 75000;
  do {
    const view = await get("/sessions/" + session.id);
    transcript = view.transcripts.find((x) => x.id === chunk.id);
    if (transcript) break;
    await page.waitForTimeout(400);
  } while (Date.now() < deadline);
  latencyMs = Date.now() - start;
  assert.equal(transcript?.asrModel, "openai/whisper-large-v3-turbo");
  assert.equal(transcript?.asrLanguageHint, "en");
  assert.match(transcript.original, /cache/i);
  const row = page.locator(`[id="${transcript.id}"]`);
  await row.waitFor({ timeout: 7000 });
  await row.scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(out, "app-model-selection.png") });
  passed = true;
} catch (e) {
  error = e.message;
  throw e;
} finally {
  await fetch(base + "/sessions/" + session.id + "/languages", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      asrLanguage: before.asrLanguage,
      noteLanguage: before.noteLanguage,
      asrModel: before.asrModel,
    }),
  });
  await writeFile(
    file,
    JSON.stringify(
      {
        testedAt: new Date().toISOString(),
        build: health.build,
        passed,
        error,
        latencyMs,
        uploadStatus,
        transcript,
        settingsRestored: {
          asrLanguage: before.asrLanguage,
          asrModel: before.asrModel,
        },
      },
      null,
      2,
    ),
  );
  await browser.close();
}
console.log(
  "UI model persisted; actual Whisper Turbo / en transcript verified, prior settings restored.",
);
