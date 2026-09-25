import assert from "node:assert/strict";
import { chromium } from "playwright-core";

const id = "a".repeat(32);
const chunks = ["one", "two"].map((name, index) => ({
  id: name, sourceId: "microphone", startMs: index * 1000,
  endMs: (index + 1) * 1000, status: "pending-asr",
}));
let automaticAsr = false;
let capture = { state: "idle", bytes: {}, autoAsr: false };
let queued = 0;
let captureStartedWithConsent = false;
const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});

try {
  const page = await browser.newPage();
  await page.route("**/api/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    let data;
    if (path === "/api/health") data = { mongo: true, gemini: false, jev: false, automaticAsr };
    else if (path === "/api/capture/status") data = capture;
    else if (path === "/api/capture/start") {
      captureStartedWithConsent = route.request().headers()["x-playback-external-consent"] === "yes";
      capture = { state: "recording", bytes: {}, autoAsr: captureStartedWithConsent };
      data = capture;
    }
    else if (path === "/api/sessions") data = [{ id, title: "Test" }];
    else if (path === `/api/sessions/${id}`)
      data = { id, title: "Test", noteMarkdown: "", noteVersion: 0,
        materials: [], transcripts: [], chunks };
    else if (path === `/api/sessions/${id}/asr/queue`) {
      assert.equal(route.request().headers()["x-playback-external-consent"], "yes");
      queued += chunks.length;
      data = { queued: chunks.length };
    } else throw new Error(`Unexpected API request: ${path}`);
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data) });
  });

  await page.goto("http://127.0.0.1:5173/");
  await page.getByRole("button", { name: /Sources/ }).click();
  const all = page.getByRole("button", { name: "Transcribe all pending audio" });
  await page.getByText("Restart the older server with pnpm.cmd dev to enable automatic ASR.").waitFor();
  assert.equal(await all.isDisabled(), true);
  assert.equal(queued, 0);

  automaticAsr = true;
  await page.reload();
  await page.getByRole("button", { name: /Sources/ }).click();
  await page.getByText("Confirm external processing consent below before sending saved audio.").waitFor();
  await page.getByRole("checkbox").check();
  await all.click();
  assert.equal(queued, 2);
  await page.getByRole("button", { name: "Transcript", exact: true }).click();
  await page.getByRole("button", { name: "Start Recording" }).click();
  assert.equal(captureStartedWithConsent, true);
  await page.getByText(/automatic ASR on/).first().waitFor();
  console.log("ASR UI check passed: old API warning, consent, backlog queue, capture consent");
} finally {
  await browser.close();
}
