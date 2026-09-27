import assert from "node:assert/strict";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";

const id = "a".repeat(32);
const insightId = "b".repeat(64);
const wav = Buffer.alloc(44 + 32_000);
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
const chunks = ["one", "two"].map((name, index) => ({
  id: name,
  sourceId: "microphone",
  sequence: index,
  startMs: index * 30_000,
  endMs: (index + 1) * 30_000,
  status: "pending-asr",
}));
chunks.push(
  {
    id: "silence-one",
    sourceId: "microphone",
    sequence: 2,
    startMs: 2000,
    endMs: 3000,
    status: "asr-empty",
  },
  {
    id: "silence-two",
    sourceId: "microphone",
    sequence: 3,
    startMs: 3000,
    endMs: 4000,
    status: "silent",
  },
  {
    id: "failed",
    sourceId: "microphone",
    sequence: 4,
    startMs: 4000,
    endMs: 5000,
    status: "asr-error",
    error: "SenseVoice HTTP 503",
  },
);
const session = {
  id,
  title: "Test",
  createdAt: "2026-09-26T08:00:00Z",
  noteMarkdown: "",
  noteVersion: 0,
  translationEnabled: false,
  translationLanguage: "zh-Hant",
  materials: [],
  terms: [{ text: "Fourier Transform", transcriptIds: ["first"], materialIds: [] }],
  termInsights: [],
  chunks,
  transcripts: [
    {
      id: "silence-one",
      sourceId: "microphone",
      startMs: 2000,
      endMs: 3000,
      original: "",
      uncertain: false,
      recognitionStatus: "asr-empty",
    },
    {
      id: "second",
      sourceId: "microphone",
      startMs: 1000,
      endMs: 2000,
      recordedAt: "2026-09-26T09:10:00Z",
      original: "Second line",
      uncertain: false,
      translation: "第二句",
      translationStatus: "completed",
      translationLanguage: "zh-Hant",
    },
    {
      id: "first",
      sourceId: "microphone",
      startMs: 0,
      endMs: 1000,
      recordedAt: "2026-09-26T08:10:00Z",
      original: "First line about Fourier Transform",
      uncertain: false,
    },
  ],
};
let capture = { state: "idle", bytes: {}, autoAsr: false };
let captureRequest;
let translationRequest;
let asrPaused = false;
let questionRequest;
let explanationRequests = 0;
const noteActions = [];
const audioRequests = [];
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  args: ["--autoplay-policy=no-user-gesture-required"],
});

try {
  const page = await browser.newPage({ timezoneId: "UTC" });
  let dialogs = 0;
  page.on("dialog", (dialog) => {
    dialogs++;
    dialog.dismiss();
  });
  page.on("request", (request) => {
    if (
      request.url().includes("/api/chunks/") &&
      request.url().endsWith("/audio")
    )
      audioRequests.push(request.url());
  });
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    let data;
    if (path.startsWith("/api/chunks/") && path.endsWith("/audio")) {
      await route.fulfill({ status: 200, contentType: "audio/wav", body: wav });
      return;
    }
    if (path === "/api/health")
      data = { mongo: true, gemini: false, jev: true, automaticAsr: !asrPaused, asrPaused };
    else if (path === "/api/capture/status") data = capture;
    else if (path === "/api/capture/start") {
      captureRequest = request.postDataJSON();
      capture = { state: "recording", bytes: {}, autoAsr: true };
      data = capture;
    } else if (path === "/api/sessions") data = [{ id, title: "Test" }];
    else if (path === "/api/groups") data = [];
    else if (path === `/api/sessions/${id}`) data = session;
    else if (path === `/api/sessions/${id}/notes`) {
      noteActions.push("save");
      session.noteMarkdown = request.postDataJSON().markdown;
      data = { version: 1 };
    } else if (path === `/api/sessions/${id}/notes/generate`) {
      noteActions.push("generate");
      await route.fulfill({ status: 409, contentType: "application/json", body: JSON.stringify({ error: "Google AI Studio key is unavailable" }) });
      return;
    } else if (path === `/api/sessions/${id}/terms/review`) {
      session.termInsights = [{ id: insightId, term: "Fourier Transform", highlight: true, transcriptIds: ["first"], materialIds: [], explanation: null, evidence: [] }];
      data = session.termInsights;
    } else if (path === `/api/sessions/${id}/terms/${insightId}/explain`) {
      explanationRequests++;
      session.termInsights[0].explanation = "A transform that represents a signal by frequency.";
      data = session.termInsights[0];
    } else if (path === `/api/sessions/${id}/ask`) {
      questionRequest = request.postDataJSON();
      await route.fulfill({ status: 409, contentType: "application/json", body: JSON.stringify({ error: "Google AI Studio key is unavailable" }) });
      return;
    }
    else if (path === `/api/sessions/${id}/translation`) {
      translationRequest = request.postDataJSON();
      session.translationEnabled = translationRequest.enabled;
      session.translationLanguage = translationRequest.language;
      data = { ok: true };
    } else throw new Error(`Unexpected API request: ${path}`);
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(data),
    });
  });

  const web =
    process.env.PLAYBACK_OFFLINE_TEST === "yes"
      ? "http://127.0.0.1:5174"
      : "http://127.0.0.1:5173";
  await page.goto(web);
  await page.locator(".audio-row").first().waitFor();
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({ path: join(tmpdir(), "playback-transcript-default-1280.png") });
  assert.equal(await page.getByRole("button", { name: "Sources" }).count(), 0);
  assert.equal(
    await page.locator(".audio-row").count(),
    2,
    "Adjacent queued audio should share one row while errors stay visible",
  );
  const play = page.locator(".audio-row .record-play").first();
  assert.equal(await play.isVisible(), false, "Audio controls should start collapsed");
  await page.locator(".audio-row .record-copy > summary").first().click();
  assert.match(await page.locator(".audio-row .record-extra").first().textContent(), /2 consecutive audio parts/);
  await play.click();
  assert.equal(await play.getAttribute("aria-pressed"), "true");
  await play.click();
  assert.equal(await play.getAttribute("aria-pressed"), "false");
  const secondAudio = page.waitForRequest("**/api/chunks/two/audio");
  await play.click();
  await secondAudio;
  assert.ok(audioRequests.some((url) => url.includes("/api/chunks/two/audio")));
  assert.equal(await page.locator(".silence-section").count(), 1);
  assert.equal(await page.getByText("No words returned by ASR").count(), 0);
  assert.equal(
    await page.locator(".silence-section").getAttribute("open"),
    null,
  );
  await page.locator(".silence-section > summary").click();
  await page
    .locator(".silence-section")
    .getByRole("button", { name: /Play audio/ })
    .waitFor();
  await page
    .locator(".audio-row")
    .filter({ hasText: "ASR failed" })
    .locator("summary")
    .click();
  await page.getByText("SenseVoice HTTP 503").waitFor();
  assert.equal(
    await page
      .getByRole("button", { name: /Retry ASR|Transcribe all pending audio/ })
      .count(),
    0,
  );
  await page.getByText("First line").waitFor();
  assert.equal(
    await page.getByRole("button", { name: "Translate", exact: true }).count(),
    0,
  );
  assert.equal(
    await page.getByRole("button", { name: "More actions" }).count(),
    0,
  );
  assert.equal(await page.locator(".timeline-day > summary").count(), 1);
  assert.equal(await page.locator(".timeline-hour > summary").count(), 2);
  assert.match(
    await page.locator(".timeline-hour > summary").first().textContent(),
    /08:00/,
  );
  for (const width of [1024, 768, 375, 320]) {
    await page.setViewportSize({ width, height: 720 });
    assert.equal(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
      true,
      `${width}px layout overflowed`,
    );
    assert.equal(
      await page.evaluate(
        () => document.documentElement.scrollHeight <= innerHeight,
      ),
      true,
      `${width}px page scrolled`,
    );
    await page
      .getByRole("navigation", { name: "Workspace views" })
      .getByRole("button", { name: "Notes" })
      .click();
    assert.equal(
      await page.getByRole("button", { name: "Save", exact: true }).isVisible(),
      true,
    );
    if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes" && width === 375)
      await page.screenshot({ path: join(tmpdir(), "playback-notes-375.png") });
    await page
      .getByRole("navigation", { name: "Workspace views" })
      .getByRole("button", { name: "Transcript" })
      .click();
    if (
      process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes" &&
      (width === 1024 || width === 375)
    )
      await page.screenshot({
        path: join(tmpdir(), `playback-transcript-${width}.png`),
      });
  }
  await page.getByRole("button", { name: "Transcript settings" }).click();
  const translation = page.getByRole("checkbox", { name: "啟用翻譯" });
  assert.equal(await translation.isDisabled(), false);
  assert.equal(
    await page.getByRole("checkbox", { name: /I confirm lecturer/ }).count(),
    0,
  );
  await translation.check();
  assert.deepEqual(translationRequest, {
    enabled: true,
    language: "zh-Hant",
  });
  assert.equal(await page.getByText("First line").count(), 1);
  const pendingTranslation = page.locator("#first .translation-pending");
  await pendingTranslation.waitFor();
  assert.equal(await pendingTranslation.isVisible(), true);
  assert.ok(Number(await pendingTranslation.evaluate((element) => getComputedStyle(element).opacity)) < 1);
  assert.equal(await page.locator("#second .translation-completed").textContent(), "第二句");
  await page.getByRole("button", { name: "Review next key terms" }).click();
  const highlight = page.locator("#first .term-highlight");
  await highlight.waitFor();
  await highlight.click();
  assert.equal(await page.getByRole("dialog", { name: "Fourier Transform explanation" }).count(), 0);
  const bounds = await highlight.boundingBox();
  await page.mouse.move(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
  await page.mouse.down();
  await page.waitForTimeout(550);
  await page.mouse.up();
  await page.getByText("A transform that represents a signal by frequency.").waitFor();
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({ path: join(tmpdir(), "playback-term-explanation-320.png") });
  assert.equal(explanationRequests, 1);
  await page.getByRole("button", { name: "Close explanation" }).click();
  await page.getByRole("button", { name: "Start recording" }).click();
  assert.deepEqual(captureRequest, { sessionId: id });
  assert.equal(dialogs, 0);
  await page.getByText("Recording", { exact: true }).waitFor();
  session.noteMarkdown = Array.from(
    { length: 80 },
    (_, index) => `Note paragraph ${index + 1}.`,
  ).join("\n\n") + `\n\nFourier Transform [ref:${insightId}]`;
  session.noteVersion = 1;
  session.currentNote = {
    author: "agent",
    transcriptIds: ["first"],
    materialIds: [],
    inputTranscriptIds: ["first"],
    inputMaterialIds: [],
    edits: [{ kind: "insert", line: 1, text: "Note paragraph 1. [first]", transcriptIds: ["first"], materialIds: [] }],
  };
  session.chunks.push(
    ...Array.from({ length: 50 }, (_, index) => ({
      id: `long-${index}`,
      sourceId: "microphone",
      sequence: index + 5,
      startMs: 30_000 * (index + 1),
      endMs: 30_000 * (index + 2),
      status: "pending-asr",
    })),
  );
  session.transcripts.push(
    ...Array.from({ length: 1266 }, (_, index) => ({
      id: `scale-${index}`,
      sourceId: "microphone",
      startMs: 1_800_000 + index * 7_110,
      endMs: 1_800_000 + (index + 1) * 7_110,
      original: `Synthetic lecture passage ${index + 1}`,
      uncertain: false,
      recognitionStatus: "recognized",
    })),
  );
  await page.setViewportSize({ width: 1440, height: 720 });
  await page.reload();
  await page.getByText("Note paragraph 80.").waitFor();
  await page.getByText("Changes in v1 · 1").click();
  await page.getByText("Added · line 1").waitFor();
  await page.getByRole("button", { name: "Open saved explanation" }).click();
  await page.getByRole("dialog", { name: "Fourier Transform explanation" }).waitFor();
  assert.equal(explanationRequests, 1, "Saved explanations should not call the model twice");
  await page.getByRole("button", { name: "Close explanation" }).click();
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollHeight <= innerHeight,
    ),
    true,
  );
  assert.equal(
    await page
      .locator(".notes-body")
      .evaluate((element) => element.scrollHeight > element.clientHeight),
    true,
  );
  assert.equal(
    await page
      .locator(".transcript-content")
      .evaluate((element) => element.scrollHeight > element.clientHeight),
    true,
  );
  assert.equal(
    await page.getByRole("button", { name: "Save", exact: true }).isVisible(),
    true,
  );
  assert.equal(await page.locator(".record-row").count() >= 1266, true);
  assert.equal(await page.getByText("02:59:54", { exact: true }).count() > 0, true);
  assert.equal(await page.locator(".silence-section:not([open])").count() > 0, true);
  await page.setViewportSize({ width: 375, height: 720 });
  assert.equal(await page.evaluate(() => document.documentElement.scrollHeight <= innerHeight), true);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await page.getByRole("navigation", { name: "Workspace views" })
    .getByRole("button", { name: "Notes" }).click();
  assert.equal(await page.getByRole("button", { name: "Save", exact: true }).isVisible(), true);
  await page.getByRole("navigation", { name: "Workspace views" })
    .getByRole("button", { name: "Transcript" }).click();
  await page.setViewportSize({ width: 1440, height: 720 });
  asrPaused = true;
  await page.reload();
  await page.getByText("ASR paused · audio saved locally").waitFor();
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await page.getByRole("textbox", { name: "Editable Markdown" }).fill("Unsaved note");
  assert.equal(await page.getByRole("textbox", { name: "Editable Markdown" }).inputValue(), "Unsaved note");
  assert.equal(await page.getByRole("button", { name: "Save", exact: true }).isEnabled(), true);
  await page.getByRole("button", { name: "Revise with AI" }).click();
  await page.locator(".global-error").waitFor();
  assert.match(await page.locator(".global-error").textContent(), /Google AI Studio key is unavailable/);
  assert.deepEqual(noteActions, ["save", "generate"]);
  await page.getByRole("button", { name: /Ask Playback/ }).first().click();
  await page.getByRole("textbox", { name: "Your question" }).fill("What happened?");
  await page.getByRole("button", { name: "Send" }).click();
  assert.equal(questionRequest.question, "What happened?");
  assert.equal(dialogs, 0);
  console.log(
    "ASR UI fixture passed: unified audio timeline, silence collapse, responsive viewport, independent scrolling, translation settings, local capture request",
  );
} finally {
  await browser.close();
}
