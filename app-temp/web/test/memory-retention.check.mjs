// Read-only, offline browser profiling. No provider, recording hardware, or real DB writes.
// The three-hour case describes the data duration; MEMORY_SECONDS is the actual soak duration.
import assert from "node:assert/strict";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import { createWriteStream } from "node:fs";
import { once } from "node:events";
import { chromium } from "playwright-core";
import { week3Fixture } from "./week3-fixture.mjs";

const url = process.env.MEMORY_BASE_URL ?? "http://127.0.0.1:5176";
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(url));
const label = process.env.MEMORY_RUN_LABEL ?? "baseline";
assert(/^[a-z0-9-]+$/.test(label));
const seconds = Number(process.env.MEMORY_SECONDS ?? 120);
assert(seconds >= 10 && seconds <= 10800);
const sectionCount = Number(process.env.MEMORY_SECTIONS ?? 0);
assert(sectionCount >= 0 && sectionCount <= 60);
const strict = process.env.MEMORY_ASSERT_BOUNDS === "yes";
const rawCount = Number(process.env.MEMORY_RAW_COUNT ?? 2700);
assert(rawCount >= 100 && rawCount <= 2700);
const dataSeconds = (rawCount / 2) * 8;
const natural =
  process.env.MEMORY_WEEK3 === "yes"
    ? await week3Fixture(
        "../data/test-audio/sampleAudio/transcript.txt",
        dataSeconds,
      )
    : null;
const folder = `output/playwright/frontend-memory/${label}`;
await mkdir(folder, { recursive: true });
const input = "../data/validation/runs/2026-09-28-notes-retention-125936";
const readJson = async (name) =>
  JSON.parse(
    (await readFile(`${input}/${name}.json`, "utf8")).replace(/^\uFEFF/, ""),
  );
const saved = await readJson("session"),
  versions = await readJson("notes");
const edits = versions.map((n) => ({
  version: n.version,
  basedOnVersion: n.basedOnVersion,
  author: n.author,
  createdAt: n.createdAt,
  inputTranscriptIds: n.inputTranscriptIds ?? [],
  inputMaterialIds: n.inputMaterialIds ?? [],
  edits: n.edits ?? [],
}));
const smallId = "memory-small",
  longId = "memory-three-hours",
  epoch = Date.parse(saved.createdAt);
const originalText =
  "A shared bottleneck has capacity R and n competing flows. The equal-share target is R/n. For n=10, R/10. Conditions, examples and sources remain readable. ";
const lectureText = (i) =>
  natural?.passages.find(
    (x) =>
      x.startMs <= Math.floor(i / 2) * 8000 &&
      x.endMs > Math.floor(i / 2) * 8000,
  )?.original ?? originalText;
const transcripts = Array.from({ length: rawCount }, (_, i) => ({
  id: `memory-${i}`,
  sourceId: i % 2 ? "system" : "microphone",
  startMs: Math.floor(i / 2) * 8000,
  endMs: Math.floor(i / 2) * 8000 + 8000,
  recordedAt: new Date(epoch + Math.floor(i / 2) * 8000).toISOString(),
  original: natural ? lectureText(i) : originalText.repeat((i % 3) + 1),
  translation: "Equal sharing requires the same bottleneck.",
  translationStatus: "completed",
  translationLanguage: "en",
  uncertain: false,
  noteStatus: "completed",
}));
const long = {
  ...saved,
  id: longId,
  title: `${dataSeconds / 3600}-hour memory fixture`,
  groupId: null,
  transcripts,
  chunks: transcripts.map((t, i) => ({
    id: t.id,
    sourceId: t.sourceId,
    startMs: t.startMs,
    endMs: t.endMs,
    recordedAt: t.recordedAt,
    sequence: i,
    status: "transcribed",
  })),
  materials: [],
  terms: [],
  termInsights: [],
  sourceGroups: [],
  translationEnabled: true,
  translationLanguage: "en",
};
for (const [i, transcript] of saved.transcripts.entries())
  long.noteMarkdown = long.noteMarkdown.replaceAll(
    transcript.id,
    transcripts[i].id,
  );
if (sectionCount) {
  const sections = Array.from({ length: sectionCount }, (_, i) => {
    const citation = "cite_" + i.toString(16).padStart(20, "0"),
      sourceIds = [transcripts[i * 2].id, transcripts[i * 2 + 1].id];
    return {
      id: `section-${i}`,
      version: 1,
      title: `Concept ${i}`,
      userEdited: false,
      markdown: `## Concept ${i}\n\n${originalText} [${citation}]\n\n| Symbol | Meaning |\n| --- | --- |\n| R | Shared capacity |\n| n | Competing flows |`,
      points: [{ id: `point-${i}`, text: originalText, sourceIds }],
      citation,
    };
  });
  long.noteMarkdown = sections.map((x) => x.markdown).join("\n\n");
  long.currentNote = {
    ...long.currentNote,
    sections,
    edits: [],
    citations: sections.map((x) => ({
      id: x.citation,
      sourceIds: x.points[0].sourceIds,
    })),
  };
}
const small = {
  ...long,
  id: smallId,
  title: "Small memory fixture",
  noteMarkdown: "# Small session\n\nOnly a small note remains.",
  noteVersion: 1,
  transcripts: [],
  chunks: [],
  currentNote: null,
};
const sessions = new Map([
  [saved.id, saved],
  [longId, long],
  [smallId, small],
]);
const cursors = new Map();
let nextCursor = 0,
  live = false,
  runningDraft = false,
  draftStep = 0,
  captureCount = 0,
  finalCount = 0;
let sourceId = saved.id,
  lastFinal = 0,
  nextFinalAt = Infinity,
  tailStart = 0,
  interimMounted = false,
  wheelWorked = false,
  cycle = 0,
  unselectedRecordingSyncs;
const start = Date.now(),
  errors = [],
  writes = [],
  metrics = [],
  requests = {},
  payloads = {},
  cancelled = {};
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
  timezoneId: "UTC",
});
await context.route(
  (url) => url.protocol === "https:" || url.hostname !== "127.0.0.1",
  (route) => route.abort(),
);
const page = await context.newPage();
page.setDefaultTimeout(15000);
page.on("pageerror", (error) => errors.push(error.message));
page.on("requestfailed", (request) => {
  const path = new URL(request.url()).pathname;
  cancelled[path] = (cancelled[path] ?? 0) + 1;
});
const cdp = await context.newCDPSession(page);
await cdp.send("Performance.enable");
function meta(session) {
  const {
    transcripts,
    chunks,
    materials,
    terms,
    termInsights,
    sourceGroups,
    ...value
  } = session;
  return value;
}
function sync(id, cursor) {
  const session = sessions.get(id);
  let state = cursors.get(cursor);
  if (!state || state.id !== id) {
    cursor = `cursor-${++nextCursor}`;
    state = {
      id,
      sent: 0,
      queue: [
        { kind: "meta", value: meta(session) },
        ...session.transcripts.map((value) => ({ kind: "transcript", value })),
        ...session.chunks.map((value) => ({ kind: "chunk", value })),
        ...session.materials.map((value) => ({ kind: "material", value })),
        ...session.terms.map((value) => ({ kind: "term", value })),
        ...session.termInsights.map((value) => ({ kind: "insight", value })),
      ],
    };
    cursors.set(cursor, state);
    // Fixture cursor inventory is bounded too; it is outside the measured browser.
    if (cursors.size > 16) cursors.delete(cursors.keys().next().value);
  }
  const changes = state.queue.splice(0, 128);
  return { cursor, reset: false, hasMore: !!state.queue.length, changes };
}
function appendFinals() {
  if (!live || Date.now() < nextFinalAt) return;
  const session = sessions.get(sourceId);
  for (const sourceId of ["microphone", "system"]) {
    const i = finalCount++,
      id = `memory-final-${i}`,
      transcript = {
        id,
        sourceId,
        startMs: tailStart,
        endMs: tailStart + 4000,
        recordedAt: new Date(epoch + tailStart).toISOString(),
        original: natural
          ? natural.passages[i % natural.passages.length].original
          : `Final ${i}: ${originalText}`,
        uncertain: false,
        noteStatus: "pending",
      };
    const chunk = {
      id,
      sourceId,
      startMs: transcript.startMs,
      endMs: transcript.endMs,
      recordedAt: transcript.recordedAt,
      status: "transcribed",
      sequence: session.chunks.length + 1,
    };
    session.transcripts.push(transcript);
    session.chunks.push(chunk);
    for (const state of cursors.values())
      if (state.id === session.id)
        state.queue.push(
          { kind: "transcript", value: transcript },
          { kind: "chunk", value: chunk },
        );
  }
  tailStart += 4000;
  lastFinal = tailStart;
  nextFinalAt = Date.now() + 4000;
}
function activity(id) {
  if (id === smallId) return [];
  return Array.from({ length: 100 }, (_, i) => ({
    id: `activity-${i}`,
    sessionId: id,
    task: i === 0 ? "Note revision" : "Jev note gate",
    provider: "offline fixture",
    model: "fixture-model",
    status: i === 0 && runningDraft ? "running" : "completed",
    startedAt: saved.createdAt,
    sourceIds: sessions
      .get(id)
      .transcripts.slice(0, 4)
      .map((t) => t.id),
    basedOnVersion: sessions.get(id).noteVersion,
    promptVersion: "memory-fixture-v1",
    promptHash: "fixture-only",
    promptText: "Effective prompt instructions. ".repeat(220),
    summary: "Read-only saved execution fixture",
    durationMs: i === 0 && runningDraft ? null : 120,
    draft:
      i === 0 && runningDraft
        ? "# Streaming note\n\n" +
          originalText.repeat(130) +
          `\n\nDraft ${draftStep++}`
        : null,
  }));
}
await page.route("**/api/**", async (route) => {
  const request = route.request(),
    address = new URL(request.url()),
    path = address.pathname;
  requests[path] = (requests[path] ?? 0) + 1;
  if (request.method() !== "GET") {
    writes.push(path);
    return route.abort();
  }
  const id = path.split("/")[3],
    session = sessions.get(id);
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
  else if (path === "/api/capture/status") {
    appendFinals();
    captureCount++;
    json = live
      ? {
          state: "recording",
          sessionId: sourceId,
          bytes: {},
          recordingElapsedMs: tailStart,
          lastFinalizedAtMs: lastFinal,
          activeSegments: [
            {
              sourceId: "microphone",
              startMs: tailStart,
              endMs: tailStart + 4000,
              recordedAt: new Date(epoch + tailStart).toISOString(),
              streaming: true,
              interimText: `Unfinished statement at the actual tail: ${captureCount}`,
            },
          ],
        }
      : { state: "idle", bytes: {} };
  } else if (path === "/api/sessions")
    json = [...sessions.values()].map(({ id, title, groupId }) => ({
      id,
      title,
      groupId,
    }));
  else if (path === "/api/groups")
    json = saved.groupId ? [{ id: saved.groupId, name: "Saved group" }] : [];
  else if (path.endsWith("/sync"))
    json = sync(id, address.searchParams.get("cursor"));
  else if (path.endsWith("/activity")) json = activity(id);
  else if (path.endsWith("/notes/edits")) json = id === smallId ? [] : edits;
  else if (path.endsWith("/notes/history")) {
    const before = Number(address.searchParams.get("before") ?? 100000),
      page = edits
        .slice()
        .reverse()
        .filter((n) => n.version < before)
        .slice(0, 20);
    json = {
      items: page.map(({ version, author, createdAt }) => ({
        version,
        author,
        createdAt,
      })),
      nextBefore:
        page.at(-1)?.version > edits[0].version ? page.at(-1).version : null,
    };
  } else if (/\/notes\/\d+$/.test(path))
    json = versions.find((n) => n.version === Number(path.split("/").at(-1)));
  else if (path.endsWith("/conversations")) json = [];
  else if (session && path === `/api/sessions/${id}`) json = session;
  else
    return route.fulfill({
      status: 404,
      json: { error: "Unsupported read-only memory fixture endpoint" },
    });
  if (
    path.endsWith("/activity") &&
    address.searchParams.get("includePrompt") === "false"
  )
    json = json.map(({ promptText, ...item }) => item);
  const body = JSON.stringify(json);
  payloads[path] = (payloads[path] ?? 0) + Buffer.byteLength(body);
  await route.fulfill({ contentType: "application/json", body });
});
const values = (response) =>
  Object.fromEntries(response.metrics.map((x) => [x.name, x.value]));
async function metric(label, gc = true) {
  const before = values(await cdp.send("Performance.getMetrics"));
  if (gc) await cdp.send("HeapProfiler.collectGarbage");
  const after = values(await cdp.send("Performance.getMetrics"));
  const row = {
    label,
    elapsedMs: Date.now() - start,
    collected: gc,
    heapBefore: before.JSHeapUsedSize,
    heapAfter: after.JSHeapUsedSize,
    taskDuration: after.TaskDuration,
    scriptDuration: after.ScriptDuration,
    layoutDuration: after.LayoutDuration,
    ...(await cdp.send("Memory.getDOMCounters")),
    elements: await page.locator("*").count(),
    rows: await page.locator("[data-virtual-row]").count(),
    orphanDiagrams: await page.locator('body > [id^="ddiagram-"]').count(),
    captureCount,
    finalCount,
  };
  metrics.push(row);
  await writeFile(
    `${folder}/progress.json`,
    JSON.stringify({ metrics, errors, writes, requests, payloads }, null, 2),
  );
  console.log(
    JSON.stringify({
      label,
      heapMiB: +(row.heapAfter / 1048576).toFixed(2),
      rows: row.rows,
      dom: row.elements,
      orphanDiagrams: row.orphanDiagrams,
      elapsed: Math.round(row.elapsedMs / 1000),
    }),
  );
  assert(row.rows < 80);
  if (strict)
    assert.equal(
      row.orphanDiagrams,
      0,
      "Mermaid left a render container in document.body",
    );
  return row;
}
async function snapshot(name) {
  const stream = createWriteStream(`${folder}/${name}.heapsnapshot`);
  let bytes = 0;
  const chunk = ({ chunk }) => {
    bytes += Buffer.byteLength(chunk);
    if (bytes > 200_000_000) throw new Error("Heap snapshot budget exceeded");
    stream.write(chunk);
  };
  cdp.on("HeapProfiler.addHeapSnapshotChunk", chunk);
  try {
    await cdp.send("HeapProfiler.takeHeapSnapshot", { reportProgress: false });
  } finally {
    cdp.off("HeapProfiler.addHeapSnapshotChunk", chunk);
    stream.end();
    await once(stream, "finish");
  }
}
async function select(title) {
  await page.getByRole("button", { name: title, exact: true }).click();
  await page
    .getByRole("banner")
    .getByRole("heading", { level: 1 })
    .getByText(title, { exact: true })
    .waitFor();
}
async function edit(text) {
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await page.getByLabel("Editable Markdown").fill(text);
  await page.getByRole("button", { name: "Preview", exact: true }).click();
}
try {
  await page.goto(url);
  await page.locator("[data-total-rows]").waitFor();
  await page.waitForTimeout(1000);
  await metric("saved-737-warm");
  await page.screenshot({ path: `${folder}/warm.png` });
  const firstVisible = await page
    .locator("[data-virtual-row] article")
    .first()
    .getAttribute("id");
  await page.locator("[data-transcript-scroller]").hover();
  await page.mouse.wheel(0, 6000);
  await page.waitForTimeout(350);
  wheelWorked =
    (await page.locator("[data-virtual-row] article").count()) > 0 &&
    (await page
      .locator("[data-virtual-row] article")
      .first()
      .getAttribute("id")) !== firstVisible;
  if (strict)
    assert(
      wheelWorked,
      "Native wheel scrolling did not replace the visible transcript rows",
    );
  await page.evaluate(() => {
    document.querySelector("[data-transcript-scroller]").scrollTop = 0;
  });
  await page.waitForTimeout(250);
  await page.getByLabel("AI activity history", { exact: true }).click();
  await page.getByRole("heading", { name: "LLM edit log" }).waitFor();
  await page
    .getByRole("region", { name: "LLM edit log" })
    .getByTestId("activity-entry")
    .first()
    .waitFor();
  await metric("saved-100-versions-activity-open");
  await page
    .getByRole("region", { name: "LLM edit log" })
    .getByTestId("activity-entry")
    .first()
    .locator("summary")
    .click();
  await page
    .getByRole("region", { name: "LLM edit log" })
    .getByRole("button", { name: "Restore this version" })
    .first()
    .waitFor();
  await metric("saved-edit-version-expanded");
  await page.keyboard.press("Escape");
  await page.mouse.move(0, 0);
  await page.waitForTimeout(250);
  await metric("activity-closed");
  for (let i = 1; i <= 30; i++) {
    await edit(
      `## Partial diagram ${i}\n\n\`\`\`mermaid\nflowchart LR\nA["Unfinished node ${i}\n\`\`\`\n`,
    );
    await page
      .getByText("Diagram syntax needs review.", { exact: true })
      .waitFor();
    if (i % 10 === 0) await metric(`invalid-diagrams-${i}`);
  }
  await edit(saved.noteMarkdown);
  await snapshot("after-invalid-diagrams");
  await select(long.title);
  await page.waitForTimeout(1000);
  await metric(`${rawCount}-${dataSeconds / 3600}-hour-data-warm`);
  sourceId = longId;
  tailStart = Math.max(...long.transcripts.map((t) => t.endMs)) + 1000;
  live = true;
  runningDraft = true;
  nextFinalAt = Date.now() + 4000;
  // Reveal an uncovered interim row, not a segment already contained in a confirmed transcript.
  await page.waitForTimeout(750);
  for (
    let i = 0;
    i < 24 &&
    !(await page.getByTestId("live-row").getByTestId("interim-text").count());
    i++
  ) {
    await page.evaluate(() => {
      const el = document.querySelector("[data-transcript-scroller]");
      el.scrollTop = el.scrollHeight;
    });
    await page.waitForTimeout(100);
  }
  interimMounted = !!(await page
    .getByTestId("live-row")
    .getByTestId("interim-text")
    .count());
  if (strict)
    assert(
      interimMounted,
      "Scrolling the actual transcript container did not mount the live tail",
    );
  if (process.env.MEMORY_NAVIGATION_CHECK === "yes") {
    await select(small.title);
    const path = `/api/sessions/${longId}/sync`,
      before = requests[path] ?? 0,
      finalsBefore = finalCount;
    await page.waitForTimeout(9000);
    unselectedRecordingSyncs = (requests[path] ?? 0) - before;
    assert(
      finalCount > finalsBefore,
      "Navigation check did not receive recording finals",
    );
    if (strict)
      assert.equal(
        unselectedRecordingSyncs,
        0,
        "Recording finals fetched the whole unselected session",
      );
    assert.equal(await page.locator("[data-virtual-row] article").count(), 0);
    await select(long.title);
    await page.waitForTimeout(400);
  }
  await cdp.send("HeapProfiler.startSampling", {
    samplingInterval: 65536,
    includeObjectsCollectedByMajorGC: true,
    includeObjectsCollectedByMinorGC: true,
  });
  const soakStart = Date.now();
  let lastSample = 0;
  while (Date.now() - soakStart < seconds * 1000) {
    cycle++;
    if (cycle % 4 === 0) {
      await page
        .getByRole("button", { name: "Live draft", exact: false })
        .click();
      await page
        .getByRole("region", { name: "Live note draft" })
        .locator("[data-markdown]")
        .waitFor();
      await page.waitForTimeout(800);
      await page.getByRole("button", { name: "Preview", exact: true }).click();
    }
    await edit(
      long.noteMarkdown +
        `\n\nLocal edit ${cycle}\n\n\`\`\`mermaid\nflowchart LR\nA["Capacity R ${cycle % 36}"] --> B["R/n"]\n\`\`\`\n`,
    );
    await page
      .getByTestId("note-content")
      .getByLabel("Rendered flowchart")
      .locator("svg")
      .last()
      .waitFor();
    await page.getByRole("button", { name: "Sources", exact: true }).click();
    const source = page
      .getByTestId("note-content")
      .getByRole("button", { name: /^Open audio sources/ })
      .first();
    if (await source.count()) {
      await source.click();
      await page
        .getByRole("dialog", { name: "Grouped audio sources" })
        .waitFor();
      await page.getByLabel("Close sources", { exact: true }).click();
    }
    await page.getByRole("button", { name: "Reading", exact: true }).click();
    if (cycle % 7 === 0) {
      await select(small.title);
      await page.waitForTimeout(300);
      await select(long.title);
      await page.waitForTimeout(500);
      await page.evaluate(() => {
        const el = document.querySelector("[data-transcript-scroller]");
        el.scrollTop = el.scrollHeight;
      });
    }
    if (cycle % 20 === 0) {
      await page.evaluate(() => {
        document.documentElement.style.zoom = "1.25";
      });
      await page.screenshot({ path: `${folder}/zoom-125.png` });
      await page.evaluate(() => {
        document.documentElement.style.zoom = "";
      });
      await page.setViewportSize({ width: 760, height: 900 });
      const headerBounds = await page.evaluate(() => {
        const title = document
            .querySelector("header h1")
            .getBoundingClientRect(),
          actions = document
            .querySelector('[data-testid="recorder"]')
            .getBoundingClientRect();
        const warning = document
            .querySelector('[data-testid="source-api-warning"]')
            ?.getBoundingClientRect(),
          sidebar = document
            .querySelector('nav[aria-label="Sessions and groups"]')
            .getBoundingClientRect();
        return {
          titleWidth: title.width,
          titleRight: title.right,
          actionsLeft: actions.left,
          warningLeft: warning?.left,
          sidebarRight: sidebar.right,
        };
      });
      if (process.env.MEMORY_ASSERT_LAYOUT === "yes") {
        assert(
          headerBounds.titleWidth === 0 ||
            headerBounds.titleRight <= headerBounds.actionsLeft + 1,
          "Recording controls overlap the session title",
        );
        if (headerBounds.warningLeft !== undefined)
          assert(
            headerBounds.warningLeft >= headerBounds.sidebarRight - 1,
            "Sidebar hides the API notice",
          );
      }
      await page.screenshot({ path: `${folder}/narrow.png` });
      await page.setViewportSize({ width: 1440, height: 1000 });
    }
    const elapsed = (Date.now() - soakStart) / 1000;
    if (elapsed - lastSample >= 15) {
      await metric(
        `soak-${Math.floor(elapsed)}s`,
        Math.floor(elapsed / 60) > Math.floor(lastSample / 60),
      );
      lastSample = elapsed;
    }
    await page.waitForTimeout(700);
  }
  const profile = await cdp.send("HeapProfiler.stopSampling");
  await writeFile(`${folder}/allocation-profile.json`, JSON.stringify(profile));
  live = false;
  runningDraft = false;
  await page.waitForTimeout(1000);
  await metric(`${rawCount}-after-soak`);
  await select(small.title);
  await page.waitForTimeout(2000);
  await metric("small-after-soak");
  await snapshot("small-after-soak");
  await page.screenshot({ path: `${folder}/small-after-soak.png` });
  assert.equal(await page.locator("[data-virtual-row]").count(), 0);
  assert(finalCount >= 2, "No confirmed incremental sources were replayed");
  assert.deepEqual(writes, []);
  assert.deepEqual(errors, []);
  await writeFile(
    `${folder}/results.json`,
    JSON.stringify(
      {
        browser: await browser.version(),
        url,
        input,
        fixtureDataHours: dataSeconds / 3600,
        realSoakSeconds: (Date.now() - soakStart) / 1000,
        naturalInput: natural && {
          path: natural.path,
          sha256: natural.sha256,
          passages: natural.passages.length,
          seconds: natural.seconds,
          sampling:
            "Performance fixture resamples exact Week 3 text onto two 8-second source timelines; not an audio/ASR alignment or generation-quality score.",
        },
        initialCounts: {
          savedTranscripts: 737,
          savedChunks: 745,
          longTranscripts: rawCount,
          sections: sectionCount,
          historyVersions: edits.length,
          activityRecords: 100,
        },
        metrics,
        interimMounted,
        wheelWorked,
        unselectedRecordingSyncs,
        cycles: cycle,
        requests,
        payloads,
        cancelled,
        writes,
        errors,
        limitations:
          "Offline fixtures, not provider/DB/hardware validation. Accelerated data plus real-time soak; not a three-hour wall-clock recording unless soak duration reaches 10800 seconds.",
      },
      null,
      2,
    ),
  );
} catch (error) {
  await writeFile(
    `${folder}/failure.txt`,
    `${error.stack}\n\n${await page.locator("body").innerText()}`,
  );
  await page.screenshot({ path: `${folder}/failure.png` });
  throw error;
} finally {
  await context.close();
  await browser.close();
}
