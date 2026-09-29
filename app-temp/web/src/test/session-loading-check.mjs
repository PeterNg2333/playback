import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { chromium } from "playwright-core";
const url = process.env.PLAYBACK_WEB_TEST_URL ?? "http://127.0.0.1:5174", folder = "output/playwright/session-loading";
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(url));
await mkdir(folder, { recursive: true });
const a = "1".repeat(32), b = "2".repeat(32), reads = [], writes = [], errors = [];
let delay = true, fail = false;
const session = id => ({ id, title: id === a ? "Loading lecture A" : "Loading lecture B", createdAt: "2026-09-29T00:00:00Z", groupId: null,
  noteMarkdown: "## Saved notes\n\nSource-backed text remains during loading.", noteVersion: 1, noteLanguage: "en", translationEnabled: false,
  translationLanguage: "zh-Hant", transcripts: [], chunks: [], terms: [], termInsights: [], materials: [], currentNote: null, sourceGroups: [] });
const browser = await chromium.launch({ headless: true, executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" });
const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
await context.route(url => url.protocol === "https:" || url.hostname !== "127.0.0.1", route => route.abort());
const page = await context.newPage(); page.on("pageerror", error => errors.push(error.message));
await page.route("**/api/**", async route => {
  const request = route.request(), path = new URL(request.url()).pathname;
  if (request.method() !== "GET") { writes.push(path); return route.abort(); }
  reads.push(path); let json;
  if (path === "/api/health") { if (delay) await new Promise(resolve => setTimeout(resolve, 800)); json = { mongo: true, gemini: false, jev: false, recordingSourceSelection: true }; }
  else if (path === "/api/capture/status") json = { state: "idle", bytes: {}, levels: {}, autoAsr: false, noSoundWarning: false, capturedThroughMs: 0, lastFinalizedAtMs: 0, activeSegments: [] };
  else if (path === "/api/sessions") json = [session(a), session(b)].map(({ id, title, groupId }) => ({ id, title, groupId }));
  else if (path === "/api/groups") json = [];
  else if (path === `/api/sessions/${a}` || path === `/api/sessions/${b}`) {
    if (path.endsWith(b)) {
      if (delay) await new Promise(resolve => setTimeout(resolve, 1200));
      if (fail) return route.fulfill({ status: 503, json: { error: "Session read unavailable; retry." } });
    }
    json = session(path.split("/").at(-1));
  } else if (path.endsWith("/activity") || path.endsWith("/conversations")) json = [];
  else return route.fulfill({ status: 404, json: { error: "Unsupported loading fixture endpoint" } });
  await route.fulfill({ json });
});
try {
  await page.goto(url); await page.getByRole("status").getByText("Loading session…", { exact: true }).waitFor();
  await page.screenshot({ path: `${folder}/startup-loading.png` });
  await page.locator(".project-name").getByText("Loading lecture A", { exact: true }).waitFor();
  await page.locator(".workspace-loading").waitFor({ state: "hidden" });
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await page.getByLabel("Editable Markdown").fill("My unsaved explanation must survive a failed session load.");
  fail = true;
  await page.getByRole("button", { name: "Loading lecture B", exact: true }).click();
  await page.locator(".workspace-loading").waitFor(); assert(await page.getByRole("button", { name: "Save", exact: true }).isDisabled());
  await page.screenshot({ path: `${folder}/switch-loading.png` });
  await page.getByRole("alert").getByText("Session read unavailable; retry.").waitFor();
  await page.locator(".workspace-loading").waitFor({ state: "hidden" });
  assert.equal(await page.getByLabel("Editable Markdown").inputValue(), "My unsaved explanation must survive a failed session load.");
  await page.screenshot({ path: `${folder}/failed-read.png` });
  fail = false; delay = false;
  await page.getByRole("button", { name: "Loading lecture B", exact: true }).click();
  await page.locator(".project-name").getByText("Loading lecture B", { exact: true }).waitFor();
  await page.locator(".workspace-loading").waitFor({ state: "hidden" });
  assert.deepEqual(writes, []); assert.deepEqual(errors, []);
  await writeFile(`${folder}/results.json`, JSON.stringify({ browser: await browser.version(), evidence: "Offline delayed/failed read fixture in production UI", reads, writes, errors }, null, 2));
  console.log("Session loading checks passed: startup, delayed switch, disabled save, failed read retains draft, explicit retry, zero provider requests.");
} finally { await context.close(); await browser.close(); }
