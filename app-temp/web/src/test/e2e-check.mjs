import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { chromium } from "playwright-core";
import { tmpdir } from "node:os";
import { join } from "node:path";

const offline = process.env.PLAYBACK_OFFLINE_TEST === "yes";
if (!offline) throw new Error("Set PLAYBACK_OFFLINE_TEST=yes and run the isolated local API/Vite test ports before E2E; external providers must stay disabled.");
const web = offline ? "http://127.0.0.1:5174" : "http://127.0.0.1:5173";
const api = offline ? "http://127.0.0.1:5079/api" : "http://127.0.0.1:5078/api";
const healthResponse = await fetch(`${api}/health`);
assert.equal(healthResponse.status, 200, "Playback API must be running");
assert.equal((await healthResponse.json()).mongo, true, "Local MongoDB must be ready");

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
let sessionId;
let groupId;
let nestedSessionId;
let captureError;

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
  const sessions = await (await fetch(`${api}/sessions`)).json();
  sessionId = sessions.find((item) => item.title === title)?.id;
  assert.match(sessionId, /^[a-f0-9]{32}$/);
  page.once("dialog", (dialog) => dialog.accept());
  await page.getByRole("button", { name: "Start recording" }).click();
  const started = await Promise.race([
    page.getByRole("button", { name: "Pause recording" }).waitFor({ timeout: 10_000 }).then(() => true),
    page.locator(".global-error").waitFor({ timeout: 10_000 }).then(() => false),
  ]);
  if (started) {
    await new Promise((resolve) => setTimeout(resolve, 1200));
    await page.getByRole("button", { name: "Pause recording" }).click();
    await page.getByRole("button", { name: "Resume recording" }).waitFor();
    assert.equal((await (await fetch(`${api}/capture/status`)).json()).state, "paused");
    await page.getByRole("button", { name: "Resume recording" }).click();
    await page.getByRole("button", { name: "Pause recording" }).waitFor();
    await new Promise((resolve) => setTimeout(resolve, 1200));
    await page.getByRole("button", { name: "Stop recording" }).click();
    await page.getByRole("button", { name: "Start recording" }).waitFor();
    assert.equal((await (await fetch(`${api}/capture/status`)).json()).state, "idle");
  } else {
    captureError = await page.locator(".global-error").textContent();
    assert.match(captureError, /Microphone could not start/);
    assert.equal((await (await fetch(`${api}/capture/status`)).json()).state, "idle");
    await page.getByRole("button", { name: "Dismiss error" }).click();
  }

  const groupName = `E2E group ${title.slice(-8)}`;
  page.once("dialog", (dialog) => dialog.accept(groupName));
  await page.getByRole("button", { name: "New group" }).click();
  const groups = await (await fetch(`${api}/groups`)).json();
  groupId = groups.find((item) => item.name === groupName)?.id;
  assert.match(groupId, /^[a-f0-9]{32}$/);
  await page.getByLabel(`Move ${title} to group`).selectOption(groupId);
  page.once("dialog", (dialog) => dialog.accept(groupName + " renamed"));
  await page.getByRole("button", { name: `Rename ${groupName}` }).click();
  await page.getByText(groupName + " renamed", { exact: true }).first().waitFor();
  page.once("dialog", (dialog) => dialog.accept(`E2E demo nested ${title.slice(-8)}`));
  await page.getByRole("button", { name: `New session in ${groupName} renamed` }).click();
  const nestedTitle = `E2E demo nested ${title.slice(-8)}`;
  await page.getByRole("button", { name: nestedTitle, exact: true }).waitFor();
  nestedSessionId = (await (await fetch(`${api}/sessions`)).json()).find((item) => item.title === nestedTitle)?.id;
  assert.ok(nestedSessionId);
  assert.equal((await (await fetch(`${api}/sessions/${nestedSessionId}`)).json()).groupId, groupId);
  await page.getByRole("button", { name: title, exact: true }).click();
  await page.getByRole("button", { name: "Sources" }).click();
  const fileChooser = page.waitForEvent("filechooser");
  await page.getByRole("button", { name: "Attach text material" }).click();
  await (await fileChooser).setFiles({
    name: "demo-lecture.txt",
    mimeType: "text/plain",
    buffer: Buffer.from(materialText),
  });
  await page.getByText(materialText).waitFor();

  await page.getByRole("button", { name: "Edit" }).click();
  await page.getByLabel("Editable Markdown").fill(markdown);
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.getByText("v1", { exact: true }).waitFor();
  await page.reload();
  await page.getByText(title, { exact: true }).first().waitFor();
  await page.getByText("v1", { exact: true }).waitFor();
  await page.locator(".session-group").filter({ hasText: groupName + " renamed" }).getByRole("button", { name: title }).waitFor();
  await page.getByRole("button", { name: "Preview" }).click();
  await page.getByRole("heading", { name: "Demo notes" }).waitFor();
  await page.getByRole("button", { name: "Sources" }).click();
  await page.getByText(materialText).waitFor();

  await page.getByRole("button", { name: "Back to transcript" }).click();
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page.getByLabel(/I confirm lecturer/).check();
  await page.getByLabel("啟用翻譯").check();
  await page.getByLabel("目標語言").selectOption("en");
  assert.equal((await (await fetch(`${api}/sessions/${sessionId}`)).json()).translationLanguage, "en");
  await page.getByLabel("啟用翻譯").uncheck();
  await page.getByRole("button", { name: "Transcript settings" }).click();
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
  await page.getByRole("button", { name: "Sources" }).click();
  await page.getByText(/Exact digital silence/).waitFor({ timeout: 10_000 });
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes") {
    const path = join(tmpdir(), "playback-desktop-check.png");
    await page.screenshot({ path, fullPage: true });
    console.log(`Desktop screenshot: ${path}`);
  }

  await page.setViewportSize({ width: 375, height: 812 });
  await page.waitForTimeout(300);
  assert.ok(await page.getByRole("button", { name: "Toggle sessions" }).isVisible());
  assert.ok((await page.locator(".sidebar").boundingBox()).x <= -200, "Closed narrow sidebar must be offscreen");
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true, "Narrow layout must not overflow horizontally");
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes") {
    await page.getByRole("button", { name: "Back to transcript" }).click();
    await page.getByRole("button", { name: "Transcript settings" }).click();
    const path = join(tmpdir(), "playback-narrow-check.png");
    await page.screenshot({ path, fullPage: true });
    console.log(`Narrow screenshot: ${path}`);
  }
  assert.deepEqual(errors, []);

  console.log("E2E passed: session groups, material, note persistence, MongoDB audio, automatic silence handling");
  if (captureError) console.log(`Capture device unavailable; UI showed the real error: ${captureError.trim()}`);
} finally {
  const cleanupFailures = [];
  try {
    const active = await (await fetch(`${api}/capture/status`)).json();
    if (active.sessionId === sessionId) await fetch(`${api}/capture/stop`, { method: "POST" });
  } catch (error) { cleanupFailures.push(`Capture cleanup failed: ${error.message}`); }
  await browser.close().catch((error) => cleanupFailures.push(error.message));
  if (sessionId) {
    try {
      const cleanup = await fetch(`${api}/testing/sessions/${sessionId}`, { method: "DELETE" });
      if (cleanup.status !== 204) cleanupFailures.push(`E2E session cleanup failed: ${await cleanup.text()}`);
    } catch (error) { cleanupFailures.push(error.message); }
  }
  if (nestedSessionId) {
    try {
      const cleanup = await fetch(`${api}/testing/sessions/${nestedSessionId}`, { method: "DELETE" });
      if (cleanup.status !== 204) cleanupFailures.push(`Nested session cleanup failed: ${await cleanup.text()}`);
    } catch (error) { cleanupFailures.push(error.message); }
  }
  if (groupId) {
    try {
      const members = await (await fetch(`${api}/sessions`)).json();
      for (const member of members.filter((item) => item.groupId === groupId && item.title.startsWith("E2E demo "))) {
        const cleanup = await fetch(`${api}/testing/sessions/${member.id}`, { method: "DELETE" });
        if (cleanup.status !== 204) cleanupFailures.push(`Group member cleanup failed: ${await cleanup.text()}`);
      }
      const cleanup = await fetch(`${api}/testing/groups/${groupId}`, { method: "DELETE" });
      if (cleanup.status !== 204) cleanupFailures.push(`E2E group cleanup failed: ${await cleanup.text()}`);
    } catch (error) { cleanupFailures.push(error.message); }
  }
  if (cleanupFailures.length) throw new Error(cleanupFailures.join("; "));
}
