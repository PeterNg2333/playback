// Real saved Week 3 snapshot + deterministic repair replay, no provider requests.
import assert from "node:assert/strict";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { chromium } from "playwright-core";
const run = "../data/validation/runs/2026-09-29-note-coverage";
const folder = "output/playwright/note-coverage";
await mkdir(folder, { recursive: true });
const before = JSON.parse(await readFile(`${run}/session-before.json`, "utf8"));
const result = JSON.parse(await readFile(`${run}/post-guard/offline-results.json`, "utf8"));
const firstNote = JSON.parse(await readFile(`${run}/post-guard/offline-note-v147.json`, "utf8"));
let session = structuredClone(before), report = structuredClone(result.initial), fail = false, writes = [], errors = [], repairBody;
const browser = await chromium.launch({ headless: true, executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe" });
try {
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  await context.route(/^https:\/\//, route => route.abort());
  const page = await context.newPage(); page.setDefaultTimeout(12000);
  page.on("pageerror", e => errors.push(e.message));
  await page.route("**/api/**", async route => {
    const request = route.request(), url = new URL(request.url()), path = url.pathname;
    if (request.method() !== "GET") writes.push(path);
    let json;
    if (path === "/api/health") json = { mongo: true, gemini: false, jev: false, sectionNotes: true, noteCoverage: true, sessionSync: true, aiActivity: true, recordingSourceSelection: true };
    else if (path === "/api/capture/status") json = { state: "idle", bytes: {} };
    else if (path === "/api/sessions") json = [{ id: before.id, title: "Week 3 coverage replay", groupId: null }];
    else if (path === "/api/groups" || /\/(activity|conversations)$/.test(path)) json = [];
    else if (path.endsWith("/sync")) {
      const { transcripts, chunks, materials, terms, termInsights, ...meta } = session;
      json = { cursor: "fixture", reset: true, hasMore: false, changes: [{ kind: "meta", value: { ...meta, groupId: null } },
        ...transcripts.map(value => ({ kind: "transcript", value })), ...chunks.map(value => ({ kind: "chunk", value }))] };
    } else if (path.endsWith("/notes/coverage")) json = report;
    else if (path.endsWith("/notes/repair")) {
      repairBody = request.postDataJSON();
      if (fail) return route.fulfill({ status: 409, json: { error: "Synthetic stale-base rejection; existing notes retained" } });
      session = { ...session, noteVersion: firstNote.version, noteMarkdown: firstNote.markdown, currentNote: firstNote };
      const remaining = report.gaps[0].sourceIds.slice(32), first = before.transcripts.find(x => x.id === remaining[0]);
      report = { ...report, version: firstNote.version, referenced: 78, unreferenced: 1078, completedWithoutReference: 1078,
        gaps: [{ ...report.gaps[0], startMs: first.startMs, sourceIds: remaining, completedWithoutReference: 1078 }] };
      json = { version: firstNote.version };
    } else if (path === `/api/sessions/${before.id}`) json = session;
    else return route.fulfill({ status: 404, json: { error: `Unhandled offline request ${path}` } });
    return route.fulfill({ json });
  });
  await page.goto("http://127.0.0.1:5177");
  await page.getByRole("button", { name: "Week 3 coverage replay", exact: true }).click();
  const coverage = page.getByRole("complementary", { name: "Transcript reference coverage" });
  await coverage.getByText("1110 transcript parts without note references", { exact: false }).waitFor();
  await page.screenshot({ path: `${folder}/coverage-closed.png` });
  await coverage.locator("summary").click();
  assert.match(await coverage.innerText(), /1110 parts were marked completed/);
  await coverage.getByRole("button", { name: /0:00–139:04/ }).click();
  await page.locator(`[id="${before.transcripts[0].id}"]`).waitFor();
  await page.screenshot({ path: `${folder}/coverage-gap-revealed.png` });
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  const editor = page.locator("textarea").first(); await editor.fill(before.noteMarkdown + "\n\nUser unsaved correction.");
  assert.equal(await coverage.getByRole("button", { name: "Repair earliest gap with AI" }).isDisabled(), true);
  await editor.fill(before.noteMarkdown);
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  await coverage.getByRole("button", { name: "Repair earliest gap with AI" }).click();
  await coverage.getByText("1078 transcript parts without note references", { exact: false }).waitFor();
  assert.deepEqual(repairBody, { basedOnVersion: 146 });
  assert.equal(session.currentNote.sections[0].markdown, before.currentNote.sections[0].markdown);
  fail = true;
  await coverage.getByRole("button", { name: "Repair earliest gap with AI" }).click();
  await coverage.getByRole("alert").getByText(/Synthetic stale-base/).waitFor();
  assert.equal(session.noteVersion, 147);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "Notes", exact: true }).click();
  assert.equal(await coverage.locator("summary").isVisible(), true);
  await page.screenshot({ path: `${folder}/coverage-narrow.png` });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  assert.deepEqual(errors, []);
  await writeFile(`${folder}/results.json`, JSON.stringify({ evidence: "Offline browser replay, saved Week 3 snapshot; no provider or DB writes", errors, writes,
    oldCompletedDetected: true, earliestSourceRevealed: true, dirtyEditProtected: true, existingSectionRetained: true, staleRepairVisible: true, narrowNoOverflow: true }, null, 2));
  console.log("Coverage UI passed: old completed gaps, earliest-source reveal, dirty edit protection, versioned repair, stale error and narrow viewport. Offline snapshot replay.");
} finally { await browser.close(); }
