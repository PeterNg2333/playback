import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import { spawn } from "node:child_process";
import path from "node:path";
import { chromium } from "playwright-core";
const out = path.resolve("app-temp/data/validation/runs/2026-09-28-repair");
const previewOnly = process.argv.includes("--preview-only");
const report = path.join(
  out,
  previewOnly ? "hardware-preview-retest.json" : "hardware-capture.json",
);
if (!process.argv.includes("--live")) {
  console.log("Offline: " + report);
  process.exit(0);
}
try {
  await readFile(report);
  if (!process.argv.includes("--refresh")) {
    console.log(
      "Reusing saved hardware result; --refresh required for new recording.",
    );
    process.exit(0);
  }
} catch {}
const session = JSON.parse(await readFile(path.join(out, "app-session.json")));
const base = "http://127.0.0.1:5081/api";
const get = async (url) => (await fetch(base + url)).json();
const post = async (url, body) => {
  const r = await fetch(base + url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}),
  });
  if (!r.ok) throw Error(await r.text());
  return r.json();
};
const before = await get("/sessions/" + session.id);
assert.equal((await get("/capture/status")).state, "idle");
const health = await get("/health");
assert.equal(health.database, "playback_e2e");
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
await page.addInitScript((id) => {
  if (window === window.top) localStorage.setItem("playback-session", id);
}, session.id);
const samples = [],
  images = new Set();
let passed = false,
  error;
const shot = async (name) => {
  if (!images.has(name)) {
    images.add(name);
    await page.screenshot({ path: path.join(out, name + ".png") });
  }
};
let timer;
try {
  await page.goto("http://127.0.0.1:5181");
  await page
    .getByRole("heading", { name: session.title, exact: true })
    .waitFor();
  await page
    .getByRole("combobox", { name: "Recording source" })
    .selectOption("system");
  await page
    .getByRole("button", { name: "Start recording", exact: true })
    .click();
  await page.getByRole("banner").getByRole("status").waitFor();
  const start = Date.now();
  let pollBusy = false;
  timer = setInterval(async () => {
    if (pollBusy) return;
    pollBusy = true;
    try {
      const capture = await get("/capture/status");
      samples.push({ at: Date.now() - start, capture });
      if (
        capture.activeSegments?.some((x) => x.interimText) &&
        !images.has("hardware-interim-visible")
      ) {
        const interim = page.getByTestId("interim-text").first();
        if (await interim.isVisible()) {
          await interim.scrollIntoViewIfNeeded();
          await shot("hardware-interim-visible");
        }
      }
      if (await page.getByTestId("note-ai-status").count()) {
        await page.getByRole("button", { name: /^Live draft/ }).click();
        if (
          await page
            .getByRole("region", { name: "Live note draft" })
            .locator("[data-markdown]")
            .count()
        )
          await shot("hardware-note-stream");
      }
    } finally {
      pollBusy = false;
    }
  }, 250);
  const audio = path
    .resolve("app-temp/data/validation/fixtures/mixed.wav")
    .replaceAll("'", "''");
  await new Promise((resolve, reject) => {
    const child = spawn(
      "powershell.exe",
      [
        "-NoProfile",
        "-Command",
        `$player=New-Object System.Media.SoundPlayer '${audio}'; $player.PlaySync(); $player.Dispose()`,
      ],
      { windowsHide: true, stdio: "ignore" },
    );
    child.on("exit", (code) =>
      code === 0 ? resolve() : reject(Error("Audio playback failed " + code)),
    );
  });
  await page.waitForTimeout(previewOnly ? 6000 : 1100);
  const paused = await post("/capture/pause");
  await page.waitForTimeout(1100);
  const still = await get("/capture/status");
  assert.equal(still.recordingElapsedMs, paused.recordingElapsedMs);
  await page.reload();
  await page
    .getByRole("heading", { name: session.title, exact: true })
    .waitFor();
  await shot("hardware-paused-reloaded");
  await post("/capture/resume");
  await page.waitForTimeout(2100);
  const resumed = await get("/capture/status");
  assert(resumed.recordingElapsedMs >= paused.recordingElapsedMs + 1900);
  assert.equal(resumed.recordingId, paused.recordingId);
  await post("/capture/stop");
  // No speech in this short second run; tests the run clock without another sample upload.
  const restart = await post("/capture/start", {
    sessionId: session.id,
    sourceMode: "system",
  });
  assert.notEqual(restart.recordingId, paused.recordingId);
  assert(restart.recordingElapsedMs < 1000);
  await page.waitForTimeout(500);
  await post("/capture/stop");
  const deadline = Date.now() + (previewOnly ? 25000 : 160000);
  let view, activity;
  do {
    view = await get("/sessions/" + session.id);
    activity = await get("/sessions/" + session.id + "/activity");
    if (await page.getByTestId("note-ai-status").count()) {
      await page.getByRole("button", { name: /^Live draft/ }).click();
      if (
        await page
          .getByRole("region", { name: "Live note draft" })
          .locator("[data-markdown]")
          .count()
      )
        await shot("hardware-note-stream");
    }
    if (
      view.transcripts.length > before.transcripts.length &&
      (previewOnly ||
        (view.noteVersion > before.noteVersion &&
          activity.some(
            (x) =>
              x.task.startsWith("Term explanation:") &&
              new Date(x.startedAt).getTime() > start,
          ) &&
          !activity.some((x) => ["running", "queued"].includes(x.status))))
    )
      break;
    await page.waitForTimeout(600);
  } while (Date.now() < deadline);
  clearInterval(timer);
  timer = null;
  assert(
    view.transcripts.length > before.transcripts.length,
    "Real captured audio must produce a final transcript",
  );
  assert(
    samples.some((x) => x.capture.activeSegments?.some((y) => y.interimText)),
    "Real provider preview must be observed",
  );
  if (!previewOnly)
    assert(
      view.noteVersion > before.noteVersion,
      "Automatic note must be saved",
    );
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("preview");
  await page.getByRole("switch", { name: "Show sources" }).uncheck();
  await shot("hardware-final");
  await page
    .getByRole("button", { name: "AI activity history", exact: true })
    .click();
  await shot("hardware-activity");
  await writeFile(
    path.join(out, "hardware-session.json"),
    JSON.stringify(view, null, 2),
  );
  await writeFile(
    path.join(out, "hardware-activity.json"),
    JSON.stringify(activity, null, 2),
  );
  passed = true;
} catch (e) {
  error = e.message;
  throw e;
} finally {
  clearInterval(timer);
  await post("/capture/stop").catch(() => {});
  await writeFile(
    report,
    JSON.stringify(
      {
        testedAt: new Date().toISOString(),
        build: health.build,
        passed,
        error,
        beforeNoteVersion: before.noteVersion,
        beforeTranscriptCount: before.transcripts.length,
        samples,
        images: [...images],
      },
      null,
      2,
    ),
  );
  await browser.close();
}
console.log(
  "Hardware capture, REST interim/final and run timer passed" +
    (previewOnly ? "; automatic AI disabled." : "; automatic notes passed."),
);
