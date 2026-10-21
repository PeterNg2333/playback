import assert from "node:assert/strict";
import { launchBrowser } from "./browser.mjs";

// Session rerun UI against fixture API data: a rerun window shown once over the chunks it covers, its first pass
// in the row details, the Rerun dialog (estimate, model, passage length, notes mode), progress with Stop, and
// Rebuild notes from the notes menu. No API, database or network.
const id = "c".repeat(32);
const chunk = (name, index, status = "transcribed") => ({
  id: `${id}-microphone-${index}-${name}`,
  sourceId: "microphone",
  sequence: index,
  startMs: index * 8000,
  endMs: (index + 1) * 8000,
  recordedAt: new Date(
    Date.parse("2026-10-20T08:00:00Z") + index * 8000,
  ).toISOString(),
  status,
});
const chunks = [chunk("a", 0), chunk("b", 1), chunk("c", 2)];
const windowId = `${id}-window-0123456789abcdef01234567`;
const session = {
  id,
  title: "Rerun",
  createdAt: "2026-10-20T08:00:00Z",
  noteMarkdown: "",
  noteVersion: 0,
  translationEnabled: false,
  translationLanguage: "zh-Hant",
  materials: [],
  terms: [],
  termInsights: [],
  chunks,
  supersessions: { [chunks[0].id]: windowId, [chunks[1].id]: windowId },
  rerun: null,
  transcripts: [
    {
      id: windowId,
      kind: "window",
      chunkIds: [chunks[0].id, chunks[1].id],
      sourceId: "microphone",
      startMs: 0,
      endMs: 16000,
      recordedAt: chunks[0].recordedAt,
      original: "The cache holds recently used data close to the processor.",
      firstPassOriginal: "The cash holds recently used data",
      reviewStatus: "completed",
      asrProvider: "openrouter",
      asrModel: "elevenlabs/scribe-v2",
      uncertain: false,
    },
    {
      id: chunks[2].id,
      sourceId: "microphone",
      startMs: 16000,
      endMs: 24000,
      recordedAt: chunks[2].recordedAt,
      original: "Misses go to memory.",
      uncertain: false,
    },
  ],
};
const rerunRequests = [];
let cancelled = 0;
const browser = await launchBrowser();
try {
  const page = await browser.newPage({ timezoneId: "UTC" });
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    let data = [];
    if (path === "/api/health")
      data = {
        mongo: true,
        gemini: true,
        jev: true,
        liveAsrPreview: true,
        automaticAsr: true,
        asrPaused: false,
        sessionAudioMix: true,
        recordingSourceSelection: true,
        sessionLanguageSettings: true,
        audioChunkMilliseconds: 8000,
        asrModels: ["qwen/qwen3-asr-1.7b", "elevenlabs/scribe-v2"],
        asr: {
          provider: "openrouter",
          model: "qwen/qwen3-asr-1.7b",
          transport: "rest",
          supportsLanguageHint: true,
        },
      };
    else if (path === "/api/capture/status")
      data = { state: "idle", bytes: {}, autoAsr: true };
    else if (path === "/api/sessions") data = [{ id, title: "Rerun" }];
    else if (path === `/api/sessions/${id}`) data = session;
    else if (
      path === `/api/sessions/${id}/rerun` &&
      request.method() === "POST"
    ) {
      const input = request.postDataJSON();
      rerunRequests.push(input);
      session.rerun = {
        id: "job",
        status: "running",
        asr: input.asr,
        asrModel: input.asrModel,
        windowSeconds: input.windowSeconds,
        notes: input.notes,
        notesStatus: "pending",
        windows: input.asr ? 3 : 0,
        completed: input.asr ? 1 : 0,
        unchanged: 0,
        skipped: 0,
        failed: 0,
        pending: input.asr ? 2 : 0,
        cancelRequested: false,
        summary: input.asr ? "1 of 3 windows re-transcribed" : "Notes running",
        createdAt: new Date().toISOString(),
      };
      data = session.rerun;
    } else if (path === `/api/sessions/${id}/rerun/cancel`) {
      cancelled++;
      session.rerun = { ...session.rerun, cancelRequested: true };
      data = session.rerun;
    } else if (request.method() !== "GET")
      throw new Error(`Unexpected API request: ${request.method()} ${path}`);
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify(data),
    });
  });
  const web =
    process.env.PLAYBACK_WEB_TEST_URL ??
    (process.env.PLAYBACK_OFFLINE_TEST === "yes"
      ? "http://127.0.0.1:5174"
      : "http://127.0.0.1:5173");
  assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(web));
  await page.goto(web);
  const windowRow = page.locator(`[data-transcript-id="${windowId}"]`);
  await windowRow.waitFor();
  assert.equal(await windowRow.count(), 1, "A rerun window must be shown once");
  assert.equal(
    await page
      .locator(
        `[data-transcript-id="${chunks[0].id}"], [data-transcript-id="${chunks[1].id}"]`,
      )
      .count(),
    0,
    "Chunks covered by a window must not show their own rows",
  );
  await page.locator(`[data-transcript-id="${chunks[2].id}"]`).waitFor();
  // Settled rows read as one passage; the window keeps its own row among the passage parts.
  await page.getByText("Original parts, translations and individual audio").click();
  const ownRow = page.locator(`[id="${windowId}"]`);
  await ownRow.locator("summary").first().click();
  await ownRow.getByText(/First-pass recognition: The cash holds/).waitFor();
  await ownRow.getByText(/Session rerun · 2 saved chunks/).waitFor();

  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page.getByRole("button", { name: "Rerun session…" }).click();
  const dialog = page.getByRole("dialog", { name: "Rerun session" });
  await dialog.waitFor();
  assert.match(
    await dialog.getByTestId("rerun-estimate").textContent(),
    /0\.4 minutes of audio in about 1 paid requests/,
  );
  await dialog
    .getByLabel("Transcription model")
    .selectOption("elevenlabs/scribe-v2");
  await dialog.getByLabel("Passage length").selectOption("60");
  await dialog.getByRole("button", { name: "Rebuild" }).click();
  await dialog.getByRole("button", { name: "Start rerun" }).click();
  await dialog.waitFor({ state: "hidden" });
  assert.deepEqual(rerunRequests.at(-1), {
    asr: true,
    asrModel: "elevenlabs/scribe-v2",
    windowSeconds: 60,
    notes: "rebuild",
  });

  const status = page.getByTestId("rerun-status");
  await status.getByText("Rerunning · 1 of 3 passages").waitFor();
  await status.getByRole("button", { name: "Stop" }).click();
  await status.getByText("Stopping rerun…").waitFor();
  assert.equal(cancelled, 1);

  session.rerun = {
    ...session.rerun,
    status: "completed",
    completedAt: new Date().toISOString(),
    cancelRequested: false,
  };
  await page.reload();
  await page.getByTestId("rerun-status").getByText("Rerun finished").waitFor();
  await page.getByLabel("Note actions", { exact: true }).click();
  await page.getByRole("button", { name: "Rebuild notes…" }).click();
  const rebuild = page.getByRole("dialog", { name: "Rebuild notes" });
  await rebuild.waitFor();
  assert.equal(
    await rebuild.getByLabel("Transcription model").count(),
    0,
    "Rebuild notes must not offer ASR options",
  );
  await rebuild.getByRole("button", { name: "Rebuild notes" }).click();
  await rebuild.waitFor({ state: "hidden" });
  assert.deepEqual(rerunRequests.at(-1), {
    asr: false,
    asrModel: null,
    windowSeconds: 45,
    notes: "rebuild",
  });
  console.log(
    "Session rerun UI passed: window shown once with its first pass, rerun dialog request, progress and stop, notes-only rebuild (fixture API)",
  );
} finally {
  await browser.close();
}
