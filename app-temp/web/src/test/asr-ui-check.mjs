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
  sequence: (index + 1) * 100000,
  startMs: index * 30_000,
  endMs: (index + 1) * 30_000,
  status: "pending-asr",
}));
chunks.push(
  {
    id: "system-quiet",
    sourceId: "system",
    sequence: 100000,
    startMs: 0,
    endMs: 1000,
    status: "asr-empty",
  },
  {
    id: "silence-one",
    sourceId: "microphone",
    sequence: 300000,
    startMs: 2000,
    endMs: 3000,
    status: "asr-empty",
  },
  {
    id: "silence-two",
    sourceId: "microphone",
    sequence: 400000,
    startMs: 3000,
    endMs: 4000,
    status: "silent",
  },
  {
    id: "failed",
    sourceId: "microphone",
    sequence: 500000,
    startMs: 4000,
    endMs: 5000,
    status: "asr-manual",
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
  materials: [
    {
      id: "activity-material",
      name: "Tutorial.txt",
      text: "Sample lesson material",
    },
  ],
  terms: [
    { text: "Fourier Transform", transcriptIds: ["first"], materialIds: [] },
  ],
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
      original: "广东话：我哋学 FFT。",
      displayOriginal: "廣東話：我哋學 FFT。",
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
let languagesRequest;
let asrPaused = false;
let recordingSourceSelection = true;
let questionRequest;
let askFailure = true;
let retryRequest;
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
    if (
      (path.startsWith("/api/chunks/") && path.endsWith("/audio")) ||
      path.startsWith(`/api/sessions/${id}/audio/segments/`)
    ) {
      await route.fulfill({ status: 200, contentType: "audio/wav", body: wav });
      return;
    }
    if (path === "/api/health")
      data = {
        mongo: true,
        gemini: false,
        jev: true,
        automaticAsr: !asrPaused,
        asrPaused,
        sessionAudioMix: true,
        recordingSourceSelection,
        sessionLanguageSettings: true,
        liveAsrPreview: true,
        audioChunkMilliseconds: 8000,
        asrModels: ["qwen/qwen3-asr-1.7b", "openai/whisper-large-v3", "openai/whisper-large-v3-turbo"],
        asr: { provider: "openrouter", model: "qwen/qwen3-asr-1.7b", transport: "rest", supportsLanguageHint: true },
      };
    else if (path === "/api/capture/status") data = capture;
    else if (path === "/api/capture/start") {
      captureRequest = request.postDataJSON();
      capture = {
        state: "recording",
        sessionId: id,
        sourceMode: captureRequest.sourceMode,
        bytes: { microphone: 32000 },
        levels: { microphone: 80 },
        capturedThroughMs: 61000,
        recordingElapsedMs: 1000,
        lastFinalizedAtMs: 60000,
        activeSegments: [],
        autoAsr: true,
      };
      data = capture;
    } else if (path === "/api/sessions") data = [{ id, title: "Test" }];
    else if (path === "/api/groups") data = [];
    else if (path === `/api/sessions/${id}`) data = session;
    else if (path === `/api/sessions/${id}/notes/edits`)
      data = [
        {
          version: 1,
          basedOnVersion: 0,
          author: "agent",
          createdAt: "2026-09-26T08:15:00Z",
          inputTranscriptIds: ["first"],
          inputMaterialIds: ["activity-material"],
          edits: [
            {
              kind: "insert",
              line: 1,
              text: "# Signal notes [first]",
              transcriptIds: ["first"],
              materialIds: [],
            },
          ],
        },
      ];
    else if (path === `/api/sessions/${id}/notes`) {
      noteActions.push("save");
      session.noteMarkdown = request.postDataJSON().markdown;
      data = { version: 1 };
    } else if (path === `/api/sessions/${id}/notes/generate`) {
      noteActions.push("generate");
      await route.fulfill({
        status: 409,
        contentType: "application/json",
        body: JSON.stringify({ error: "Google AI Studio key is unavailable" }),
      });
      return;
    } else if (path === `/api/sessions/${id}/terms/review`) {
      session.termInsights = [
        {
          id: insightId,
          term: "Fourier Transform",
          highlight: true,
          jevProbability: 0.84,
          jevRank: "high",
          jevConfidence: 0.53,
          jevModel: "test-model",
          jevCached: false,
          decisionRule:
            "Explain probability >= 75%; rank high or medium; category confidence >= 50%.",
          rankedAt: "2026-09-26T08:16:00Z",
          transcriptIds: ["first"],
          materialIds: [],
          explanation: null,
          evidence: [],
        },
      ];
      data = session.termInsights;
    } else if (path === `/api/sessions/${id}/terms/${insightId}/explain`) {
      explanationRequests++;
      session.termInsights[0].explanation =
        "A transform that represents a signal by frequency.";
      data = session.termInsights[0];
    } else if (path === `/api/sessions/${id}/ask`) {
      questionRequest = request.postDataJSON();
      if (askFailure) {
        await route.fulfill({
          status: 409,
          contentType: "application/json",
          body: JSON.stringify({ error: "Vertex AI key is unavailable" }),
        });
        return;
      }
      data = {
        answer: "The lecture introduced Fourier Transform [first].",
        evidence: [{ kind: "lecture", id: "first", label: "00:00–00:01" }],
        inference: true,
      };
    } else if (path === `/api/sessions/${id}/chunks/retry`) {
      retryRequest = request.postDataJSON();
      session.chunks.find((chunk) => chunk.id === "failed").status =
        "pending-asr";
      data = {};
    } else if (path === `/api/sessions/${id}/languages`) {
      languagesRequest = request.postDataJSON();
      Object.assign(session, languagesRequest);
      data = { ok: true };
    } else if (path === `/api/sessions/${id}/translation`) {
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
  await page.locator("#second").getByText("廣東話：我哋學 FFT。", { exact: true }).waitFor();
  assert.equal(await page.locator("#second").getByText("广东话：我哋学 FFT。", { exact: true }).count(), 0);
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({
      path: join(tmpdir(), "playback-transcript-default-1280.png"),
    });
  assert.equal(await page.getByRole("button", { name: "Sources" }).count(), 0);
  assert.equal(
    await page.locator(".audio-row").count(),
    2,
    "Queued and failed audio should remain separate from collapsed quiet audio",
  );
  assert.equal(await page.locator(".quiet-section").count(), 1);
  assert.equal(
    await page.locator(".quiet-section").first().getAttribute("open"),
    null,
  );
  assert.equal(
    await page.locator("#first .record-time").textContent(),
    "08:10:00",
  );
  const play = page.locator(".audio-row .record-play").first();
  assert.equal(
    await play.isVisible(),
    true,
    "Saved audio should be playable without expanding a row",
  );
  await page.locator(".audio-row .record-copy > summary").first().click();
  assert.match(
    await page.locator(".audio-row .record-extra").first().textContent(),
    /2 audio parts grouped/,
  );
  await play.click();
  await page.waitForFunction(
    () =>
      document
        .querySelector(".audio-row .record-play")
        ?.getAttribute("aria-pressed") === "true",
  );
  assert.equal(await play.getAttribute("aria-pressed"), "true");
  await play.click();
  assert.equal(await play.getAttribute("aria-pressed"), "false");
  const secondAudio = page.waitForRequest("**/api/chunks/two/audio");
  await play.click();
  await secondAudio;
  assert.ok(audioRequests.some((url) => url.includes("/api/chunks/two/audio")));
  assert.equal(
    await page.locator("audio").count(),
    1,
    "The footer and rows must share one player",
  );
  const mixedAudio = page.waitForRequest((request) =>
    request.url().endsWith(`/api/sessions/${id}/audio/segments/0`),
  );
  await page.getByLabel("Playback mode").click();
  assert.equal(await page.locator(".player-mode-menu").isVisible(), true,
    "Playback source dropdown must open above the footer");
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({ path: join(tmpdir(), "playback-source-dropdown.png") });
  await page.getByRole("button", { name: "Full session" }).click();
  await mixedAudio;
  const systemAudio = page.waitForRequest((request) =>
    request
      .url()
      .includes(`/api/sessions/${id}/audio/segments/0?source=system`),
  );
  await page.getByLabel("Playback mode").click();
  await page
    .getByRole("combobox", { name: "Audio sources" })
    .selectOption("system");
  await systemAudio;
  assert.equal(
    await page.locator(".playback-footer").getByText("Session audio").count(),
    1,
  );
  const selectedAudio = page.waitForRequest("**/api/chunks/one/audio");
  await play.click();
  await selectedAudio;
  await page
    .getByLabel("Playback mode")
    .filter({ hasText: "Selected audio" })
    .waitFor();
  assert.equal(
    await page.getByLabel("Playback mode").textContent(),
    "Selected audio",
  );
  assert.equal(
    await page
      .locator(".quiet-section > summary")
      .filter({ hasText: "No audio" })
      .count(),
    1,
  );
  assert.equal(await page.getByText("No words returned by ASR").count(), 0);
  await page.locator(".quiet-section > summary").click();
  await page
    .locator(".quiet-section")
    .getByRole("button", { name: /Play audio/ })
    .first()
    .waitFor();
  await page
    .locator(".audio-row")
    .filter({ hasText: "ASR stopped" })
    .locator("summary")
    .click();
  await page.getByText("SenseVoice HTTP 503").waitFor();
  await page.locator(".asr-manual-row .retry-asr").click();
  assert.deepEqual(retryRequest, { chunkIds: ["failed"] });
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
  assert.equal(await page.locator(".timeline-minute").count(), 0);
  assert.equal(await page.locator(".timeline-entries").count(), 2);
  assert.match(
    await page.locator(".timeline-hour > summary").first().textContent(),
    /08:00/,
  );
  for (const width of [1024, 768, 375, 320]) {
    await page.setViewportSize({ width, height: 720 });
    assert.ok(
      (await page.locator(".playback-footer").boundingBox()).height <= 84,
      `${width}px player must stay compact`,
    );
    const inset = await page
      .locator(".timeline-entries")
      .first()
      .evaluate((element) => {
        const rows = element.closest(".rows");
        return (
          element.getBoundingClientRect().left -
          rows.getBoundingClientRect().left -
          parseFloat(getComputedStyle(rows).paddingLeft)
        );
      });
    assert.ok(
      inset <= 12,
      `${width}px transcript must use shallow indentation`,
    );
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
  await page.getByText("ASR: qwen/qwen3-asr-1.7b (openrouter, rest)").waitFor();
  const asrLanguage = page.getByRole("combobox", { name: "ASR spoken language" });
  const noteLanguage = page.getByRole("combobox", { name: "Notes output language" });
  await asrLanguage.selectOption("yue");
  assert.deepEqual(languagesRequest, { asrLanguage: "yue", noteLanguage: "zh-Hant", asrModel: null });
  await noteLanguage.selectOption("en");
  assert.deepEqual(languagesRequest, { asrLanguage: "yue", noteLanguage: "en", asrModel: null });
  await page.getByRole("combobox", { name: "ASR model" }).selectOption("openai/whisper-large-v3-turbo");
  assert.deepEqual(languagesRequest, { asrLanguage: "yue", noteLanguage: "en", asrModel: "openai/whisper-large-v3-turbo" });
  await asrLanguage.selectOption("yue-en");
  await page.reload();
  await page.getByRole("button", { name: "Transcript settings" }).click();
  assert.equal(await asrLanguage.inputValue(), "yue-en");
  assert.equal(await page.getByRole("combobox", { name: "ASR model" }).inputValue(), "openai/whisper-large-v3-turbo");
  assert.equal(await noteLanguage.inputValue(), "en");
  const translationLanguage = page.getByRole("combobox", { name: "Translation target language" });
  for (const language of ["yue-Hant", "zh-Hans", "en", "zh-Hant"]) {
    await translationLanguage.selectOption(language);
    assert.deepEqual(translationRequest, { enabled: false, language });
  }
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({ path: join(tmpdir(), "playback-language-settings-320.png") });
  const translation = page.getByRole("checkbox", { name: "Enable translation" });
  await page.waitForFunction(() => !document.querySelector('input[type="checkbox"]')?.disabled);
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
  assert.ok(
    Number(
      await pendingTranslation.evaluate(
        (element) => getComputedStyle(element).opacity,
      ),
    ) < 1,
  );
  assert.equal(
    await page.locator("#second .translation-completed").textContent(),
    "第二句",
  );
  // Simulate the background Jev/LLM result arriving in the session snapshot.
  session.termInsights = [{ id: insightId, term: "Fourier Transform", highlight: true,
    jevProbability: 0.84, jevRank: "high", jevConfidence: 0.53, jevModel: "test-model", jevCached: false,
    decisionRule: "Explain probability >= 75%; rank high or medium; category confidence >= 50%.",
    rankedAt: "2026-09-26T08:16:00Z", transcriptIds: ["first"], materialIds: [],
    explanation: "A transform that represents a signal by frequency.", evidence: [] }];
  const highlight = page.locator("#first .term-highlight");
  await highlight.waitFor();
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await highlight.hover();
  await page
    .getByText("A transform that represents a signal by frequency.")
    .waitFor();
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({
      path: join(tmpdir(), "playback-term-explanation-320.png"),
    });
  assert.equal(explanationRequests, 0, "Hover must only read the automatically saved explanation");
  await page.getByRole("button", { name: "Close explanation" }).click();
  await page.getByRole("navigation", { name: "Workspace views" }).getByRole("button", { name: "Notes", exact: true }).click();
  await page.getByRole("button", { name: "AI activity history", exact: true }).click();
  await page.getByText("v1 · AI edit", { exact: true }).click();
  await page
    .locator(".activity-edit pre")
    .filter({ hasText: "# Signal notes [first]" })
    .waitFor();
  const trace = page
    .locator(".activity-entry")
    .filter({ hasText: "Fourier Transform" });
  await trace.locator("summary").click();
  await trace.getByText("84%", { exact: true }).waitFor();
  await trace.getByText("53%", { exact: true }).waitFor();
  await trace.getByText("high", { exact: true }).waitFor();
  assert.equal(
    await page
      .locator(".activity-content input, .activity-content textarea")
      .count(),
    0,
  );
  await trace.locator(".decision-scores").scrollIntoViewIfNeeded();
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({
      path: join(tmpdir(), "playback-activity-320.png"),
    });
  await trace.getByRole("button", { name: "08:10:00", exact: true }).click();
  await page.locator("#first").waitFor();
  await page.keyboard.press("Escape");
  await page.getByRole("navigation", { name: "Workspace views" }).getByRole("button", { name: "Notes", exact: true }).click();
  await page.getByRole("button", { name: "AI activity history", exact: true }).click();
  await page.getByText("v1 · AI edit", { exact: true }).click();
  await page.getByRole("button", { name: "Tutorial.txt", exact: true }).click();
  await page.locator("#activity-material").waitFor();
  assert.equal(
    await page.locator("#session-materials").getAttribute("open"),
    "",
    "Activity material links must open the materials section",
  );
  await page.keyboard.press("Escape");
  await page
    .getByRole("combobox", { name: "Recording source" })
    .selectOption("microphone");
  await page.getByRole("button", { name: "Start recording" }).click();
  assert.deepEqual(captureRequest, { sessionId: id, sourceMode: "microphone" });
  assert.equal(
    await page.getByRole("combobox", { name: "Recording source" }).isDisabled(),
    true,
  );
  assert.equal(dialogs, 0);
  await page.locator(".record-indicator").waitFor();
  await page.getByLabel("Recording elapsed 00:01").waitFor();
  capture.recordingElapsedMs = 3661000;
  await page.getByLabel("Recording elapsed 01:01:01").waitFor();
  await page.getByRole("img", { name: "Audio signal quiet" }).waitFor();
  await page.getByText("Listening for sound").waitFor();
  const placeholderStarted = Date.now();
  capture = {
    ...capture,
    capturedThroughMs: 60100,
    activeSegments: [
      {
        sourceId: "microphone",
        startMs: 60000,
        endMs: 60100,
        recordedAt: "2026-09-26T08:11:00Z",
        streaming: false,
      },
    ],
  };
  await page.locator(".live-segment").waitFor();
  assert.ok(
    Date.now() - placeholderStarted < 1000,
    "Speech placeholder should appear within one second",
  );
  await page.getByText("Speech detected · recording audio").waitFor();
  capture = {
    ...capture,
    capturedThroughMs: 61100,
    activeSegments: [
      { ...capture.activeSegments[0], endMs: 61100, streaming: true },
    ],
  };
  await page.getByRole("img", { name: "Audio signal received" }).waitFor();
  await page.getByText("Speech active · recording audio").waitFor();
  assert.match(await page.locator(".live-segment").textContent(), /08:11:00/);
  assert.ok(
    Number(
      await page
        .locator(".live-segment")
        .evaluate((element) => getComputedStyle(element).opacity),
    ) < 1,
  );
  capture = { ...capture, activeSegments: [{ ...capture.activeSegments[0], interimText: "我哋 study FFT" }] };
  await page.locator(".interim-text").getByText("我哋 study FFT", { exact: true }).waitFor();
  capture = { ...capture, activeSegments: [{ ...capture.activeSegments[0], interimText: "我哋 study FFT and frequency" }] };
  await page.locator(".interim-text").getByText("我哋 study FFT and frequency", { exact: true }).waitFor();
  assert.equal(await page.locator(".interim-text").count(), 1);
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes") {
    await page.locator(".live-segment").scrollIntoViewIfNeeded();
    await page.locator(".live-segment").screenshot({ path: join(tmpdir(), "playback-interim-row.png") });
  }
  session.chunks.push({
    id: "saved-live",
    sourceId: "microphone",
    sequence: 900000,
    startMs: 60000,
    endMs: 61100,
    recordedAt: "2026-09-26T08:11:00Z",
    status: "pending-asr",
  });
  capture = { ...capture, lastFinalizedAtMs: 61100 };
  await page.locator(".live-segment").getByText("我哋 study FFT and frequency", { exact: true }).waitFor();
  session.chunks.find(chunk => chunk.id === "saved-live").status = "asr-error";
  await page.waitForTimeout(4300);
  await page.locator(".live-segment").getByText("我哋 study FFT and frequency", { exact: true }).waitFor();
  session.chunks.find(chunk => chunk.id === "saved-live").status = "transcribed";
  session.transcripts.push({ id: "saved-live", sourceId: "microphone", startMs: 60000, endMs: 61100,
    recordedAt: "2026-09-26T08:11:00Z", original: "我哋 study FFT and frequency", uncertain: false });
  await page.locator("#saved-live").waitFor();
  await page.locator(".live-segment").waitFor({ state: "detached" });
  assert.equal(await page.getByText("我哋 study FFT and frequency", { exact: true }).count(), 1);
  await page.locator(".record-row").filter({ hasText: "08:11:00" }).waitFor();
  await page.setViewportSize({ width: 320, height: 720 });
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
    true,
  );
  session.noteMarkdown =
    Array.from(
      { length: 80 },
      (_, index) => `Note paragraph ${index + 1}.`,
    ).join("\n\n") +
    `\n\nFourier Transform [ref:${insightId}]\n\nLegacy citation [${"c".repeat(32)}, 5000-6000 ms; ${"c".repeat(32)}, 5000-6000 ms]`;
  session.noteVersion = 1;
  session.currentNote = {
    author: "agent",
    transcriptIds: ["first"],
    materialIds: [],
    inputTranscriptIds: ["first"],
    inputMaterialIds: [],
    edits: [
      {
        kind: "insert",
        line: 1,
        text: "Note paragraph 1. [first]",
        transcriptIds: ["first"],
        materialIds: [],
      },
    ],
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
    {
      id: "c".repeat(32),
      sourceId: "microphone",
      startMs: 5000,
      endMs: 6000,
      original: "Legacy source",
      uncertain: false,
      recognitionStatus: "recognized",
    },
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
  await page
    .getByRole("button", { name: "Jump to source at 08:00:05" })
    .waitFor();
  assert.equal(
    await page.locator(".markdown-preview").getByText("c".repeat(32)).count(),
    0,
  );
  await page.getByText("Changes in v1 · 1").click();
  await page.getByText("Added · line 1").waitFor();
  await page.getByRole("button", { name: "Open saved explanation" }).click();
  await page
    .getByRole("dialog", { name: "Fourier Transform explanation" })
    .waitFor();
  assert.equal(
    explanationRequests,
    0,
    "Saved explanations should not call the model twice",
  );
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
  assert.equal((await page.locator(".record-row").count()) >= 1266, true);
  assert.equal((await page.locator(".quiet-section").count()) > 0, true);
  await page.setViewportSize({ width: 375, height: 720 });
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollHeight <= innerHeight,
    ),
    true,
  );
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
    true,
  );
  await page
    .getByRole("navigation", { name: "Workspace views" })
    .getByRole("button", { name: "Notes" })
    .click();
  assert.equal(
    await page.getByRole("button", { name: "Save", exact: true }).isVisible(),
    true,
  );
  await page
    .getByRole("navigation", { name: "Workspace views" })
    .getByRole("button", { name: "Transcript" })
    .click();
  await page.setViewportSize({ width: 1440, height: 720 });
  asrPaused = true;
  await page.reload();
  await page.getByText("ASR paused · audio saved locally").waitFor();
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await page
    .getByRole("textbox", { name: "Editable Markdown" })
    .fill("Unsaved note");
  assert.equal(
    await page.getByRole("textbox", { name: "Editable Markdown" }).inputValue(),
    "Unsaved note",
  );
  assert.equal(
    await page.getByRole("button", { name: "Save", exact: true }).isEnabled(),
    true,
  );
  await page.getByRole("button", { name: "Revise with AI" }).click();
  await page.locator(".global-error").waitFor();
  assert.match(
    await page.locator(".global-error").textContent(),
    /Google AI Studio key is unavailable/,
  );
  assert.deepEqual(noteActions, ["save", "generate"]);
  await page
    .getByRole("button", { name: /Ask Playback/ })
    .first()
    .click();
  await page
    .getByRole("textbox", { name: "Your question" })
    .fill("What happened?");
  await page.getByRole("button", { name: "Send" }).click();
  assert.equal(questionRequest.question, "What happened?");
  await page
    .locator(".chat-error")
    .getByText("Vertex AI key is unavailable")
    .waitFor();
  assert.equal(
    await page.getByRole("textbox", { name: "Your question" }).inputValue(),
    "What happened?",
  );
  askFailure = false;
  await page.getByRole("button", { name: "Send" }).click();
  await page
    .locator(".answer").getByText(/The lecture introduced Fourier Transform/)
    .waitFor();
  assert.equal(await page.locator(".chat-error").count(), 0);
  assert.equal(dialogs, 0);
  await page.setViewportSize({ width: 320, height: 720 });
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
    true,
    "320px recording controls must fit",
  );
  recordingSourceSelection = false;
  capture = { state: "idle", bytes: {} };
  await page.reload();
  await page.getByRole("combobox", { name: "Recording source" }).waitFor();
  await page.locator("#first").waitFor();
  assert.equal(
    await page.getByRole("combobox", { name: "Recording source" }).isDisabled(),
    false,
    "Source preference must remain selectable with an old API",
  );
  await page.getByRole("combobox", { name: "Recording source" }).selectOption("system");
  assert.equal(await page.getByRole("button", { name: "Start recording" }).isDisabled(), true,
    "Old APIs must not silently record both sources instead of the selected source");
  await page.getByText(/Recording source selection needs the updated API/).waitFor();
  if (process.env.PLAYBACK_CAPTURE_SCREENSHOTS === "yes")
    await page.screenshot({ path: join(tmpdir(), "playback-source-old-api.png") });
  console.log(
    "ASR UI fixture passed: flat timeline, compact player, read-only edit/Jev activity, recording mode and old API compatibility, manual retry, live meter, Ask Playback states, responsive viewport",
  );
} finally {
  await browser.close();
}
