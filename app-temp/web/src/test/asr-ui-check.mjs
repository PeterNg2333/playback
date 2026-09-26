import assert from "node:assert/strict";
import { chromium } from "playwright-core";

const id = "a".repeat(32);
const chunks = ["one", "two"].map((name, index) => ({
  id: name, sourceId: "microphone", sequence: index,
  startMs: index * 1000, endMs: (index + 1) * 1000, status: "pending-asr",
}));
const session = {
  id, title: "Test", createdAt: "2026-09-26T08:00:00Z", noteMarkdown: "", noteVersion: 0,
  externalProcessingConsent: false, translationEnabled: false, translationLanguage: "zh-Hant",
  materials: [], terms: [], chunks,
  transcripts: [
    { id: "second", sourceId: "microphone", startMs: 1000, endMs: 2000, recordedAt: "2026-09-26T09:10:00Z", original: "Second line", uncertain: false },
    { id: "first", sourceId: "microphone", startMs: 0, endMs: 1000, recordedAt: "2026-09-26T08:10:00Z", original: "First line", uncertain: false },
  ],
};
let capture = { state: "idle", bytes: {}, autoAsr: false };
let captureRequest;
let translationRequest;
const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});

try {
  const page = await browser.newPage({ timezoneId: "UTC" });
  page.on("dialog", (dialog) => dialog.accept());
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    let data;
    if (path === "/api/health") data = { mongo: true, gemini: false, jev: false, automaticAsr: true };
    else if (path === "/api/capture/status") data = capture;
    else if (path === "/api/capture/start") {
      captureRequest = request.postDataJSON();
      capture = { state: "recording", bytes: {}, autoAsr: true };
      data = capture;
    }
    else if (path === "/api/sessions") data = [{ id, title: "Test" }];
    else if (path === "/api/groups") data = [];
    else if (path === `/api/sessions/${id}`) data = session;
    else if (path === `/api/sessions/${id}/consent`) {
      session.externalProcessingConsent = request.postDataJSON().confirmed;
      data = { ok: true };
    }
    else if (path === `/api/sessions/${id}/translation`) {
      translationRequest = request.postDataJSON();
      session.translationEnabled = translationRequest.enabled;
      session.translationLanguage = translationRequest.language;
      data = { ok: true };
    }
    else throw new Error(`Unexpected API request: ${path}`);
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data) });
  });

  const web = process.env.PLAYBACK_OFFLINE_TEST === "yes" ? "http://127.0.0.1:5174" : "http://127.0.0.1:5173";
  await page.goto(web);
  await page.getByRole("button", { name: "Sources" }).click();
  await page.getByText(/Saved audio remains linked to this session/).waitFor();
  assert.equal(await page.getByRole("button", { name: /Retry ASR|Transcribe all pending audio/ }).count(), 0);
  await page.getByRole("button", { name: "Back to transcript" }).click();
  await page.getByText("First line").waitFor();
  assert.equal(await page.getByRole("button", { name: "Translate", exact: true }).count(), 0);
  assert.equal(await page.getByRole("button", { name: "More actions" }).count(), 0);
  assert.equal(await page.locator(".timeline-day > summary").count(), 1);
  assert.equal(await page.locator(".timeline-hour > summary").count(), 2);
  assert.match(await page.locator(".timeline-hour > summary").first().textContent(), /08:00/);
  await page.getByRole("button", { name: "Transcript settings" }).click();
  const translation = page.getByRole("checkbox", { name: "啟用翻譯" });
  assert.equal(await translation.isDisabled(), true);
  await page.getByRole("checkbox", { name: /I confirm lecturer/ }).check();
  await translation.check();
  assert.deepEqual(translationRequest, { enabled: true, language: "zh-Hant", consentConfirmed: true });
  assert.equal(await page.getByText("First line").count(), 1);
  await page.getByRole("button", { name: "Start recording" }).click();
  assert.deepEqual(captureRequest, { sessionId: id, consentConfirmed: true });
  await page.getByText("Recording", { exact: true }).waitFor();
  console.log("ASR UI fixture passed: source links, timeline, session translation settings, local capture request");
} finally {
  await browser.close();
}
