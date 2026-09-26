import assert from "node:assert/strict";
import { chromium } from "playwright-core";

const id = "a".repeat(32);
const chunks = ["one", "two"].map((name, index) => ({
  id: name, sourceId: "microphone", startMs: index * 1000,
  endMs: (index + 1) * 1000, status: "pending-asr",
}));
let capture = { state: "idle", bytes: {}, autoAsr: false };
let captureSentConsentHeader = false;
const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});

try {
  const page = await browser.newPage({ timezoneId: "UTC" });
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    let data;
    if (path === "/api/health") data = { mongo: true, gemini: true, jev: false, automaticAsr: true };
    else if (path === "/api/capture/status") data = capture;
    else if (path === "/api/capture/start") {
      captureSentConsentHeader = route.request().headers()["x-playback-external-consent"] === "yes";
      capture = { state: "recording", bytes: {}, autoAsr: true };
      data = capture;
    }
    else if (path === "/api/sessions") data = [{ id, title: "Test" }];
    else if (path === `/api/sessions/${id}`)
      data = { id, title: "Test", createdAt: "2026-09-26T08:00:00Z", noteMarkdown: "", noteVersion: 0,
        materials: [], transcripts: [
          { id: "second", sourceId: "microphone", startMs: 1000, endMs: 2000, recordedAt: "2026-09-26T09:10:00Z", original: "Second line", uncertain: false },
          { id: "first", sourceId: "microphone", startMs: 0, endMs: 1000, recordedAt: "2026-09-26T08:10:00Z", original: "First line", uncertain: false },
        ], chunks };
    else throw new Error(`Unexpected API request: ${path}`);
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data) });
  });

  await page.goto("http://127.0.0.1:5173/");
  await page.getByRole("button", { name: /Sources/ }).click();
  await page.getByText(/Saved audio is queued for ASR automatically/).waitFor();
  assert.equal(await page.getByRole("button", { name: /Retry ASR|Transcribe all pending audio/ }).count(), 0);
  await page.getByRole("button", { name: "Transcript", exact: true }).click();
  await page.getByText("First line").waitFor();
  assert.equal(await page.getByRole("button", { name: "Translate" }).first().isEnabled(), true);
  assert.equal(await page.locator(".consent").count(), 0);
  assert.equal(await page.locator(".timeline-day > summary").count(), 1);
  assert.equal(await page.locator(".timeline-hour > summary").count(), 2);
  assert.match(await page.locator(".timeline-hour > summary").first().textContent(), /08:00/);
  await page.getByRole("button", { name: "Start Recording" }).click();
  assert.equal(captureSentConsentHeader, false);
  await page.getByText(/automatic ASR on/).first().waitFor();
  console.log("ASR UI check passed: automatic queue, no manual retry, timeline, direct capture, no consent gate");
} finally {
  await browser.close();
}
