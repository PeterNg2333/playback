// Same saved 737-transcript / 745-chunk snapshot as the previous retention audit.
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { chromium } from "playwright-core";
import assert from "node:assert/strict";
const input =
  "../data/validation/runs/2026-09-28-notes-retention-125936/session.json";
const session = JSON.parse(
  (await readFile(input, "utf8")).replace(/^\uFEFF/, ""),
);
const folder = "output/playwright/notes-redesign";
await mkdir(folder, { recursive: true });
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
  timezoneId: "UTC",
});
const page = await context.newPage(),
  errors = [],
  writes = [],
  results = [];
let live = false,
  step = 0,
  initialized = false;
page.on("pageerror", (e) => errors.push(e.message));
await context.route(/^https:\/\//, (r) => r.abort());
await page.route("**/api/**", async (route) => {
  const request = route.request(),
    path = new URL(request.url()).pathname;
  if (request.method() !== "GET") {
    writes.push(path);
    return route.abort();
  }
  let json;
  if (path === "/api/health")
    json = {
      mongo: true,
      gemini: false,
      jev: false,
      aiActivity: true,
      sessionSync: true,
      sectionNotes: true,
    };
  else if (path === "/api/capture/status")
    json = live
      ? {
          state: "recording",
          sessionId: session.id,
          bytes: {},
          recordingElapsedMs: 300000,
          activeSegments: [
            {
              sourceId: "microphone",
              startMs: 0,
              endMs: 5000,
              recordedAt: session.createdAt,
              streaming: true,
              interimText: "Unfinished source statement " + step++,
            },
          ],
        }
      : { state: "idle", bytes: {} };
  else if (path === "/api/sessions")
    json = [{ id: session.id, title: session.title, groupId: session.groupId }];
  else if (path === "/api/groups")
    json = session.groupId
      ? [{ id: session.groupId, name: "Saved group" }]
      : [];
  else if (path.endsWith("/sync")) {
    const {
      transcripts,
      chunks,
      materials,
      terms,
      termInsights,
      sourceGroups,
      ...meta
    } = session;
    json = {
      cursor: "saved-fixture",
      reset: false,
      hasMore: false,
      changes: initialized
        ? []
        : [
            { kind: "meta", value: meta },
            ...transcripts.map((value) => ({ kind: "transcript", value })),
            ...chunks.map((value) => ({ kind: "chunk", value })),
            ...materials.map((value) => ({ kind: "material", value })),
            ...terms.map((value) => ({ kind: "term", value })),
            ...termInsights.map((value) => ({ kind: "insight", value })),
          ],
    };
    initialized = true;
  } else if (path.endsWith("/activity") || path.endsWith("/conversations"))
    json = [];
  else if (path === `/api/sessions/${session.id}`) json = session;
  else
    return route.fulfill({
      status: 404,
      json: { error: "Unimplemented read-only fixture request" },
    });
  return route.fulfill({ json });
});
const cdp = await context.newCDPSession(page);
await cdp.send("Performance.enable");
async function metric(label) {
  const before = Object.fromEntries(
    (await cdp.send("Performance.getMetrics")).metrics.map((x) => [
      x.name,
      x.value,
    ]),
  );
  await cdp.send("HeapProfiler.collectGarbage");
  const after = Object.fromEntries(
    (await cdp.send("Performance.getMetrics")).metrics.map((x) => [
      x.name,
      x.value,
    ]),
  );
  const row = {
    label,
    elements: await page.locator("*").count(),
    rows: await page.locator("[data-virtual-row]").count(),
    heapBeforeGc: before.JSHeapUsedSize,
    heapAfterGc: after.JSHeapUsedSize,
    taskDuration: after.TaskDuration,
    scriptDuration: after.ScriptDuration,
    ...(await cdp.send("Memory.getDOMCounters")),
  };
  results.push(row);
  assert(row.rows < 80);
  return row;
}
try {
  await page.goto("http://127.0.0.1:5174");
  await page.locator("[data-total-rows]").waitFor();
  await page.waitForTimeout(600);
  await metric("737 saved snapshot idle");
  await page.screenshot({ path: `${folder}/saved-snapshot-737.png` });
  live = true;
  await page.waitForTimeout(4000);
  await metric("737 snapshot interim replay");
  for (let cycle = 1; cycle <= 24; cycle++) {
    await page
      .getByRole("combobox", { name: "Note view" })
      .selectOption("markdown");
    await page
      .getByLabel("Editable Markdown")
      .fill(session.noteMarkdown + `\n\nLocal edit ${cycle}`);
    await page
      .getByRole("combobox", { name: "Note view" })
      .selectOption("preview");
    await page.getByRole("switch", { name: "Show sources" }).uncheck();
    if (cycle % 4 === 0)
      await metric(`737 snapshot ${cycle} edit/preview cycles`);
  }
  live = false;
  await page.waitForTimeout(1500);
  await metric("737 settled final GC");
  assert.deepEqual(errors, []);
  assert.deepEqual(writes, []);
  await writeFile(
    `${folder}/snapshot-737-metrics.json`,
    JSON.stringify(
      {
        input,
        transcripts: session.transcripts.length,
        chunks: session.chunks.length,
        browser: await browser.version(),
        results,
        errors,
        writes,
        limitations:
          "Same source snapshot; compare old audit cautiously: Reading and incremental transport intentionally differ. Fixture replay, not actual live recording or a long-duration leak proof.",
      },
      null,
      2,
    ),
  );
  console.log(
    JSON.stringify(
      results.map((x) => ({
        label: x.label,
        rows: x.rows,
        dom: x.elements,
        heapMiB: +(x.heapAfterGc / 1048576).toFixed(2),
        listeners: x.jsEventListeners,
      })),
    ),
  );
} finally {
  await context.close();
  await browser.close();
}
