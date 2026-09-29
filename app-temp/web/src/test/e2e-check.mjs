import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { chromium } from "playwright-core";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { mkdir, writeFile } from "node:fs/promises";

const offline = process.env.PLAYBACK_OFFLINE_TEST === "yes";
const skipCapture = process.env.PLAYBACK_E2E_SKIP_CAPTURE === "yes";
const retain = process.env.PLAYBACK_E2E_RETAIN === "yes";
if (!offline)
  throw new Error(
    "Set PLAYBACK_OFFLINE_TEST=yes and run the isolated local API/Vite test ports before E2E; external providers must stay disabled.",
  );
const web = offline ? "http://127.0.0.1:5174" : "http://127.0.0.1:5173";
const api = offline ? "http://127.0.0.1:5079/api" : "http://127.0.0.1:5078/api";
const healthResponse = await fetch(`${api}/health`);
assert.equal(healthResponse.status, 200, "Playback API must be running");
assert.equal(
  (await healthResponse.json()).mongo,
  true,
  "Local MongoDB must be ready",
);

const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
let sessionId;
let groupId;
let nestedSessionId;
const captureErrors = [];
let passed = false;
let failure;
let page;
const evidence = fileURLToPath(new URL("../../../data/validation/runs/2026-09-29-week3/", import.meta.url));
await mkdir(evidence, { recursive: true });

try {
  const context = await browser.newContext();
  page = await context.newPage();
  // Enable interception before navigation; Edge can otherwise open the native
  // picker before its asynchronous interception registration has completed.
  page.on("filechooser", () => {});
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  const title = `E2E demo ${randomUUID().slice(0, 8)}`;
  const materialText =
    "Demo lecture: local audio chunks are saved before transcription.";
  const markdown = "# Demo notes\n\nAudio chunks are saved locally.";

  await page.goto(web);
  await page.getByRole("heading", { name: "Transcript" }).waitFor();
  await page.getByRole("button", { name: "New session", exact: true }).click();
  await page
    .getByRole("dialog")
    .getByRole("textbox", { name: "Session title" })
    .fill(title);
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await page.getByText(title, { exact: true }).first().waitFor();
  const sessions = await (await fetch(`${api}/sessions`)).json();
  sessionId = sessions.find((item) => item.title === title)?.id;
  assert.match(sessionId, /^[a-f0-9]{32}$/);
  if (!skipCapture) {
    for (const mode of ["microphone", "system", "both"]) {
      await page
        .getByRole("combobox", { name: "Recording source" })
        .selectOption(mode);
      page.once("dialog", (dialog) => dialog.accept());
      await page.getByRole("button", { name: "Start recording" }).click();
      const started = await Promise.race([
        page
          .getByRole("button", { name: "Pause recording" })
          .waitFor({ timeout: 10_000 })
          .then(() => true),
        page
          .locator(".global-error")
          .waitFor({ timeout: 10_000 })
          .then(() => false),
      ]);
      if (started) {
        const running = await (await fetch(`${api}/capture/status`)).json();
        assert.equal(running.sourceMode, mode);
        const expectedSources =
          mode === "both" ? ["microphone", "system"] : [mode];
        assert.ok(Object.keys(running.bytes).length > 0);
        assert.ok(
          Object.keys(running.bytes).every((source) =>
            expectedSources.includes(source),
          ),
          "Recording must not open an unselected source",
        );
        assert.equal(
          await page
            .getByRole("combobox", { name: "Recording source" })
            .isDisabled(),
          true,
        );
        await new Promise((resolve) => setTimeout(resolve, 1200));
        await page.getByRole("button", { name: "Pause recording" }).click();
        await page.getByRole("button", { name: "Resume recording" }).waitFor();
        assert.equal(
          (await (await fetch(`${api}/capture/status`)).json()).sourceMode,
          mode,
        );
        assert.equal(
          (await (await fetch(`${api}/capture/status`)).json()).state,
          "paused",
        );
        await page.getByRole("button", { name: "Resume recording" }).click();
        await page.getByRole("button", { name: "Pause recording" }).waitFor();
        const resumed = await (await fetch(`${api}/capture/status`)).json();
        assert.equal(resumed.sourceMode, mode);
        assert.ok(
          Object.keys(resumed.bytes).every((source) =>
            expectedSources.includes(source),
          ),
          "Resume must keep the selected sources",
        );
        await new Promise((resolve) => setTimeout(resolve, 1200));
        await page.getByRole("button", { name: "Stop recording" }).click();
        await page.getByRole("button", { name: "Start recording" }).waitFor();
        assert.equal(
          (await (await fetch(`${api}/capture/status`)).json()).state,
          "idle",
        );
      } else {
        const captureError = await page.locator(".global-error").textContent();
        captureErrors.push(`${mode}: ${captureError.trim()}`);
        assert.match(captureError, /No audio source could start/);
        assert.equal(
          (await (await fetch(`${api}/capture/status`)).json()).state,
          "idle",
        );
        await page.getByRole("button", { name: "Dismiss error" }).click();
      }
    }
  }

  const groupName = `E2E group ${title.slice(-8)}`;
  await page.getByRole("button", { name: "New group" }).click();
  await page
    .getByRole("dialog")
    .getByRole("textbox", { name: "Group name" })
    .fill(groupName);
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await page.getByRole("button", { name: `Collapse ${groupName}` }).waitFor();
  const groups = await (await fetch(`${api}/groups`)).json();
  groupId = groups.find((item) => item.name === groupName)?.id;
  assert.match(groupId, /^[a-f0-9]{32}$/);
  const groupFolder = page.locator(".session-group").filter({
    has: page.getByRole("button", {
      name: `Collapse ${groupName}`,
      exact: true,
    }),
  });
  await page
    .getByRole("button", { name: title, exact: true })
    .dragTo(groupFolder.locator(".folder-row"));
  await groupFolder.getByRole("button", { name: title, exact: true }).waitFor();
  await groupFolder
    .getByRole("button", { name: title, exact: true })
    .dragTo(page.locator(".sessions-section .sidebar-heading"));
  await page
    .locator(".sessions-section")
    .getByRole("button", { name: title, exact: true })
    .waitFor();
  await page
    .locator(".sessions-section")
    .getByRole("button", { name: title, exact: true })
    .dragTo(groupFolder.locator(".folder-row"));
  await page
    .locator(`summary[aria-label="Group options for ${groupName}"]`)
    .click();
  await page.getByRole("button", { name: "Rename group" }).click();
  await page
    .getByRole("dialog")
    .getByRole("textbox", { name: "Group name" })
    .fill(groupName + " renamed");
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await page
    .getByText(groupName + " renamed", { exact: true })
    .first()
    .waitFor();
  await page
    .getByRole("button", { name: `New session in ${groupName} renamed` })
    .click();
  const nestedTitle = `E2E demo nested ${title.slice(-8)}`;
  await page
    .getByRole("dialog")
    .getByRole("textbox", { name: "Session title" })
    .fill(nestedTitle);
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await page.getByRole("button", { name: nestedTitle, exact: true }).waitFor();
  nestedSessionId = (await (await fetch(`${api}/sessions`)).json()).find(
    (item) => item.title === nestedTitle,
  )?.id;
  assert.ok(nestedSessionId);
  assert.equal(
    (await (await fetch(`${api}/sessions/${nestedSessionId}`)).json()).groupId,
    groupId,
  );
  await page.getByRole("button", { name: title, exact: true }).click();
  // TranscriptContent is keyed by session. Opening the old session's details
  // during a switch loses that open state when the selected session mounts.
  await page.getByRole("heading", { name: title, exact: true }).waitFor();
  await page.locator(".workspace-loading").waitFor({ state: "hidden" });
  await page.locator("#session-materials > summary").click();
  const fileChooser = page.waitForEvent("filechooser", { timeout: 10000 }).catch(() => null);
  await page.getByRole("button", { name: "Attach text material" }).click();
  const picked = await fileChooser;
  assert(picked, "Materials file picker did not open after session loading completed");
  await picked.setFiles({
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
  await page
    .locator(".session-group")
    .filter({ hasText: groupName + " renamed" })
    .getByRole("button", { name: title })
    .waitFor();
  await page.getByRole("button", { name: "Preview" }).click();
  await page.getByRole("heading", { name: "Demo notes" }).waitFor();
  await page.locator("#session-materials > summary").click();
  await page.getByText(materialText).waitFor();
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page.getByLabel("ASR spoken language").selectOption("yue-en");
  await page.getByLabel("ASR model", { exact: true }).selectOption("openai/whisper-large-v3-turbo");
  await page.getByLabel("Notes output language").selectOption("zh-Hans");
  await page.waitForFunction(async (id) => {
    const saved = await (await fetch(`/api/sessions/${id}`)).json();
    return saved.asrLanguage === "yue-en" && saved.noteLanguage === "zh-Hans" && saved.asrModel === "openai/whisper-large-v3-turbo";
  }, sessionId);
  await page.reload();
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page.waitForFunction(() => document.getElementById("asr-language")?.disabled === false);
  assert.equal(await page.getByLabel("ASR spoken language").inputValue(), "yue-en");
  assert.equal(await page.getByLabel("ASR model", { exact: true }).inputValue(), "openai/whisper-large-v3-turbo");
  assert.equal(await page.getByLabel("Notes output language").inputValue(), "zh-Hans");
  await page.getByLabel("Enable translation").check();
  await page.getByLabel("Translation target language").selectOption("en");
  await page.waitForFunction(async (id) => {
    const response = await fetch(`/api/sessions/${id}`);
    return response.ok && (await response.json()).translationLanguage === "en";
  }, sessionId);
  assert.equal(
    (await (await fetch(`${api}/sessions/${sessionId}`)).json())
      .translationLanguage,
    "en",
  );
  await page.getByLabel("Enable translation").uncheck();
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
    sessionId,
    sourceId: "e2e_demo",
    sequence: 0,
    startMs: 0,
    endMs: 1000,
    sha256: createHash("sha256").update(wav).digest("hex"),
  }))
    form.set(key, String(value));
  form.set("file", new Blob([wav], { type: "audio/wav" }), "silent-demo.wav");
  const upload = await fetch(`${api}/chunks`, { method: "POST", body: form });
  const uploaded = await upload.json();
  assert.equal(
    upload.status,
    202,
    `Audio upload returned ${upload.status}: ${uploaded.error ?? "unknown error"}`,
  );
  const { id: chunkId } = uploaded;

  const deadline = Date.now() + 60_000;
  let savedSession;
  do {
    const response = await fetch(`${api}/sessions/${sessionId}`);
    assert.equal(response.status, 200);
    savedSession = await response.json();
    const chunk = savedSession.chunks.find(
      (savedChunk) => savedChunk.id === chunkId,
    );
    if (chunk?.status === "silent") break;
    if (chunk?.status === "asr-error") throw new Error(chunk.error);
    await new Promise((resolve) => setTimeout(resolve, 500));
  } while (Date.now() < deadline);
  assert.equal(
    savedSession.chunks.find((chunk) => chunk.id === chunkId)?.status,
    "silent",
  );
  assert.equal(
    savedSession.transcripts.length,
    0,
    "Digital silence must not create a transcript",
  );
  assert.equal(savedSession.noteMarkdown, markdown);
  assert.deepEqual(
    savedSession.materials.map((material) => material.text),
    [materialText],
  );
  const quietAudio = page
    .locator(".quiet-section")
    .filter({ hasText: "No audio" })
    .first();
  await quietAudio.locator("summary").waitFor({ timeout: 10_000 });
  await quietAudio.locator("summary").click();
  const savedAudio = page.waitForResponse((response) =>
    response.url().endsWith(`/api/chunks/${chunkId}/audio`),
  );
  await page.locator(`.record-play[data-chunk-id="${chunkId}"]`).click();
  assert.ok(
    [200, 206].includes((await savedAudio).status()),
    "Saved chunk must play from the real API",
  );
  const tone = Buffer.from(wav);
  for (let offset = 44; offset < tone.length; offset += 2)
    tone.writeInt16LE(5000, offset);
  for (const sourceId of ["microphone", "system"]) {
    const toneForm = new FormData();
    for (const [key, value] of Object.entries({
      sessionId,
      sourceId,
      sequence: 0,
      startMs: 2000,
      endMs: 3000,
      sha256: createHash("sha256").update(tone).digest("hex"),
    }))
      toneForm.set(key, String(value));
    toneForm.set(
      "file",
      new Blob([tone], { type: "audio/wav" }),
      "synthetic-tone.wav",
    );
    assert.equal(
      (await fetch(`${api}/chunks`, { method: "POST", body: toneForm })).status,
      202,
    );
  }
  await page.getByLabel("Playback mode").click();
  await page
    .getByRole("combobox", { name: "Audio sources" })
    .locator('option[value="system"]')
    .waitFor({ state: "attached" });
  const mixedAudio = page.waitForResponse((response) =>
    response.url().endsWith(`/api/sessions/${sessionId}/audio/segments/0`),
  );
  await page.getByRole("button", { name: "Full session" }).click();
  assert.equal(
    (await mixedAudio).status(),
    200,
    "Full-session playback must load synchronized audio",
  );
  const systemAudio = page.waitForResponse((response) =>
    response
      .url()
      .includes(`/api/sessions/${sessionId}/audio/segments/0?source=system`),
  );
  await page.getByLabel("Playback mode").click();
  await page
    .getByRole("combobox", { name: "Audio sources" })
    .selectOption("system");
  assert.equal(
    (await systemAudio).status(),
    200,
    "Source selection must use the same footer player",
  );
  assert.equal(await page.locator("audio").count(), 1);
  await page.getByRole("button", { name: "AI activity history", exact: true }).click();
  await page.getByRole("heading", { name: "LLM edit log" }).waitFor();
  await page.getByText("v1 · Manual edit", { exact: true }).click();
  await page
    .locator(".activity-edit pre")
    .filter({ hasText: "# Demo notes" })
    .waitFor();
  assert.equal(
    await page
      .locator(".activity-content textarea, .activity-content input")
      .count(),
    0,
    "Activity is read-only",
  );
  await page.keyboard.press("Escape");
  if (!retain) {
  await page
    .locator(`summary[aria-label="Group options for ${groupName} renamed"]`)
    .click();
  await page.getByRole("button", { name: "Delete group" }).click();
  await page
    .getByRole("dialog", { name: `Delete ${groupName} renamed?` })
    .getByRole("button", { name: "Delete group" })
    .click();
  await page
    .locator(".sessions-section")
    .getByRole("button", { name: title, exact: true })
    .waitFor();
  await page
    .locator(".sessions-section")
    .getByRole("button", { name: nestedTitle, exact: true })
    .waitFor();
  const retained = await (await fetch(`${api}/sessions/${sessionId}`)).json();
  assert.equal(retained.groupId, null);
  assert.equal(retained.noteMarkdown, markdown);
  assert.deepEqual(
    retained.materials.map((material) => material.text),
    [materialText],
  );
  assert.equal(
    (await (await fetch(`${api}/sessions/${nestedSessionId}`)).json()).groupId,
    null,
  );
  groupId = null;
  }
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes") {
    const path = join(evidence, "offline-e2e-desktop.png");
    await page.screenshot({ path, fullPage: true });
    console.log(`Desktop screenshot: ${path}`);
  }

  await page.setViewportSize({ width: 375, height: 812 });
  await page.waitForTimeout(300);
  assert.ok(
    await page.getByRole("button", { name: "Toggle sessions" }).isVisible(),
  );
  assert.ok(
    (await page.locator(".sidebar").boundingBox()).x <= -200,
    "Closed narrow sidebar must be offscreen",
  );
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
    true,
    "Narrow layout must not overflow horizontally",
  );
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes") {
    await page.getByRole("button", { name: "Transcript settings" }).click();
    const path = join(evidence, "offline-e2e-narrow.png");
    await page.screenshot({ path, fullPage: true });
    console.log(`Narrow screenshot: ${path}`);
  }
  assert.deepEqual(errors, []);
  passed = true;

  console.log(
    "E2E passed: session groups, material, note persistence and activity history, MongoDB audio, automatic silence handling",
  );
  if (skipCapture)
    console.log("Local microphone capture skipped for this E2E run");
  for (const captureError of captureErrors)
    console.log(
      `Capture device unavailable; UI showed the real error: ${captureError}`,
    );
} catch (error) {
  failure = error.message;
  await page?.screenshot({ path: join(evidence, "offline-e2e-failure.png") }).catch(() => {});
  throw error;
} finally {
  const cleanupFailures = [];
  try {
    const active = await (await fetch(`${api}/capture/status`)).json();
    if (active.sessionId === sessionId)
      await fetch(`${api}/capture/stop`, { method: "POST" });
  } catch (error) {
    cleanupFailures.push(`Capture cleanup failed: ${error.message}`);
  }
  await browser.close().catch((error) => cleanupFailures.push(error.message));
  if (sessionId && !retain) {
    try {
      const cleanup = await fetch(`${api}/testing/sessions/${sessionId}`, {
        method: "DELETE",
      });
      if (cleanup.status !== 204)
        cleanupFailures.push(
          `E2E session cleanup failed: ${await cleanup.text()}`,
        );
    } catch (error) {
      cleanupFailures.push(error.message);
    }
  }
  if (nestedSessionId && !retain) {
    try {
      const cleanup = await fetch(
        `${api}/testing/sessions/${nestedSessionId}`,
        { method: "DELETE" },
      );
      if (cleanup.status !== 204)
        cleanupFailures.push(
          `Nested session cleanup failed: ${await cleanup.text()}`,
        );
    } catch (error) {
      cleanupFailures.push(error.message);
    }
  }
  if (groupId && !retain) {
    try {
      const members = await (await fetch(`${api}/sessions`)).json();
      for (const member of members.filter(
        (item) =>
          item.groupId === groupId && item.title.startsWith("E2E demo "),
      )) {
        const cleanup = await fetch(`${api}/testing/sessions/${member.id}`, {
          method: "DELETE",
        });
        if (cleanup.status !== 204)
          cleanupFailures.push(
            `Group member cleanup failed: ${await cleanup.text()}`,
          );
      }
      const cleanup = await fetch(`${api}/testing/groups/${groupId}`, {
        method: "DELETE",
      });
      if (cleanup.status !== 204)
        cleanupFailures.push(
          `E2E group cleanup failed: ${await cleanup.text()}`,
        );
    } catch (error) {
      cleanupFailures.push(error.message);
    }
  }
  if (cleanupFailures.length) throw new Error(cleanupFailures.join("; "));
  await writeFile(join(evidence, "offline-e2e.json"), JSON.stringify({ testedAt: new Date().toISOString(), passed, failure, offline, skipCapture, retain,
    sessionId, nestedSessionId, groupId, captureErrors, evidence: "Production browser, real localhost API/MongoDB; synthetic local WAV only, no providers or microphone/system capture." }, null, 2));
  if (retain) console.log(`Test sessions and group retained: ${sessionId}, ${nestedSessionId}, ${groupId}`);
}
