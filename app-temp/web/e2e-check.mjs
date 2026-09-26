import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { chromium } from "playwright-core";

const web = "http://127.0.0.1:5173";
const api = "http://127.0.0.1:5078/api";
const healthResponse = await fetch(`${api}/health`);
assert.equal(healthResponse.status, 200, "Playback API must be running");
assert.equal((await healthResponse.json()).mongo, true, "Local MongoDB must be ready");

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});

try {
  const context = await browser.newContext();
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  const title = `E2E demo ${randomUUID().slice(0, 8)}`;
  const materialText = "Demo lecture: local audio chunks are saved before transcription.";
  const markdown = "# Demo notes\n\nAudio chunks are saved locally.";

  await page.goto(web);
  await page.getByRole("heading", { name: "Transcript" }).waitFor();
  page.once("dialog", (dialog) => dialog.accept(title));
  await page.getByRole("button", { name: "New session" }).click();
  await page.getByText(title, { exact: true }).first().waitFor();
  const sessionId = await page.getByRole("combobox", { name: "Select session" }).inputValue();
  assert.match(sessionId, /^[a-f0-9]{32}$/);

  await page.getByRole("button", { name: /Sources/ }).click();
  const fileChooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Attach text material" }).click();
  await (await fileChooser).setFiles({
    name: "demo-lecture.txt",
    mimeType: "text/plain",
    buffer: Buffer.from(materialText),
  });
  await page.getByText(materialText).waitFor();

  await page.getByRole("button", { name: "Markdown" }).click();
  await page.getByLabel("Editable Markdown").fill(markdown);
  await page.getByRole("button", { name: "Save revision" }).click();
  await page.getByText("Revision 1").waitFor();
  await page.reload();
  await page.getByText(title, { exact: true }).first().waitFor();
  await page.getByText("Revision 1").waitFor();
  await page.getByRole("button", { name: "Preview" }).click();
  await page.getByRole("heading", { name: "Demo notes" }).waitFor();
  await page.getByRole("button", { name: /Sources/ }).click();
  await page.getByText(materialText).waitFor();

  const wav = Buffer.alloc(44 + 16_000 * 2);
  wav.write("RIFF", 0);
  wav.writeUInt32LE(wav.length - 8, 4);
  wav.write("WAVEfmt ", 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(16_000, 24);
  wav.writeUInt32LE(32_000, 28);
  wav.writeUInt16LE(2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write("data", 36);
  wav.writeUInt32LE(32_000, 40);
  const form = new FormData();
  for (const [key, value] of Object.entries({
    sessionId, sourceId: "e2e_demo", sequence: 0, startMs: 0, endMs: 1000,
    sha256: createHash("sha256").update(wav).digest("hex"),
  })) form.set(key, String(value));
  form.set("file", new Blob([wav], { type: "audio/wav" }), "silent-demo.wav");
  const upload = await fetch(`${api}/chunks`, { method: "POST", body: form });
  const uploaded = await upload.json();
  assert.equal(upload.status, 202, `Audio upload returned ${upload.status}: ${uploaded.error ?? "unknown error"}`);
  const { id: chunkId } = uploaded;

  const deadline = Date.now() + 60_000;
  let savedSession;
  do {
    const response = await fetch(`${api}/sessions/${sessionId}`);
    assert.equal(response.status, 200);
    savedSession = await response.json();
    const chunk = savedSession.chunks.find((savedChunk) => savedChunk.id === chunkId);
    if (chunk?.status === "silent") break;
    if (chunk?.status === "asr-error") throw new Error(chunk.error);
    await new Promise((resolve) => setTimeout(resolve, 500));
  } while (Date.now() < deadline);
  assert.equal(savedSession.chunks.find((chunk) => chunk.id === chunkId)?.status, "silent");
  assert.equal(savedSession.transcripts.length, 0, "Digital silence must not create a transcript");
  assert.equal(savedSession.noteMarkdown, markdown);
  assert.deepEqual(savedSession.materials.map((material) => material.text), [materialText]);
  await page.getByText("silent", { exact: true }).waitFor({ timeout: 10_000 });

  const realSessionId = process.env.PLAYBACK_E2E_REAL_SESSION_ID;
  if (realSessionId) {
    const response = await fetch(`${api}/sessions/${realSessionId}`);
    assert.equal(response.status, 200, "Real speech session must exist");
    const realSession = await response.json();
    assert.ok(realSession.transcripts.length > 0, "Real speech must have a transcript");
    assert.ok(realSession.transcripts.every((entry) => entry.original && !entry.original.includes("<|")));
    await page.getByRole("combobox", { name: "Select session" }).selectOption(realSessionId);
    await page.getByRole("button", { name: "Transcript", exact: true }).click();
    await page.locator(".transcript-row").first().waitFor();
    assert.ok(await page.locator(".timeline-day > summary").count() > 0);
    assert.ok(await page.locator(".timeline-hour > summary").count() > 0);
    assert.ok(await page.locator(".transcript-row .original").first().textContent());
  }
  assert.deepEqual(errors, []);

  console.log(`E2E passed: UI create, material, note persistence, MongoDB audio, automatic silence handling (${sessionId})`);
  if (realSessionId) console.log("E2E passed: real speech transcript and timeline");
} finally {
  await browser.close();
}
