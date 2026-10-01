// Offline browser behavior/performance. Generated content quality requires a separate real-provider review.
import assert from "node:assert/strict";
import { mkdir, writeFile, readFile } from "node:fs/promises";
import { chromium } from "playwright-core";

const folder = "output/playwright/notes-redesign";
await mkdir(folder, { recursive: true });
const start = Date.now(),
  errors = [],
  writes = [],
  measurements = [];
const id = "a".repeat(32),
  smallId = "b".repeat(32),
  group = "study-group",
  createdAt = "2026-09-28T09:00:00Z";
const key = (i) =>
  `${id}-${i % 2 ? "system" : "microphone"}-${i}-${"c".repeat(64)}`;
let count = 1350,
  live = false,
  flowFail = false,
  edits = 0;
function lecture(n) {
  const transcripts = Array.from({ length: n }, (_, i) => ({
    id: key(i),
    sourceId: i % 2 ? "system" : "microphone",
    startMs: Math.floor(i / 2) * 8000,
    endMs: Math.floor(i / 2) * 8000 + 8000,
    recordedAt: new Date(
      Date.parse(createdAt) + Math.floor(i / 2) * 8000,
    ).toISOString(),
    original: `Source ${i}: A bottleneck shares capacity R among n flows. Conditions and examples are preserved across chunks. ${"Lecture explanation. ".repeat((i % 4) + 1)}`,
    translation: `Translation of source ${i}.`,
    translationLanguage: "en",
    translationStatus: "completed",
    uncertain: i % 71 === 0,
    noteStatus: "completed",
  }));
  const cite = "cite_" + "f".repeat(20),
    ids = [key(0), key(2), key(Math.min(1200, n - 2))];
  const section = {
    id: "fair-sharing",
    version: 1,
    title: "Fair sharing",
    userEdited: false,
    markdown: `## Fair sharing

A bottleneck limits throughput. The target is **R/n** when n flows share that bottleneck. **Example:** n=10 gives R/10. Conditions matter. [${cite}]

| Symbol | Meaning |
| --- | --- |
| R | Shared bottleneck capacity |
| n | Number of competing flows |

\`\`\`mermaid
flowchart LR
 A["One bottleneck: R"] --> B["n competing flows"]
 B --> C["Target per flow: R/n"]
\`\`\`
Source-supported relationship. [${cite}]`,
    points: [
      {
        id: "formula",
        text: "The target is R/n; n=10 gives R/10.",
        sourceIds: ids,
      },
    ],
  };
  return {
    id,
    title: "Three-hour lecture fixture",
    groupId: group,
    createdAt,
    noteMarkdown: section.markdown,
    noteVersion: 120,
    noteLanguage: "en",
    translationEnabled: true,
    translationLanguage: "en",
    materials: [],
    transcripts,
    chunks: transcripts.map((t, i) => ({
      ...t,
      sequence: i,
      status: "transcribed",
    })),
    terms: [],
    sourceGroups: [],
    termInsights: [
      {
        id: "d".repeat(64),
        term: "bottleneck",
        highlight: true,
        outputLanguage: "en",
        explanationVersion: "term-detail-v2",
        transcriptIds: [key(0)],
        materialIds: [],
        explanationSummary: "The resource that limits throughput.",
        explanation:
          "The resource that limits throughput.\n\n## Conditions\n\nCapacity is shared among flows using the same limiting link. R/n is an equal-share target, not a universal guarantee.\n\n## Example\n\nFor ten flows, the target is R/10. Symbols and limitations remain explicit.",
        evidence: [],
      },
    ],
    currentNote: {
      author: "agent",
      basedOnVersion: 119,
      transcriptIds: ids,
      materialIds: [],
      edits: [],
      sections: [section],
      citations: [{ id: cite, sourceIds: ids }],
      coverage: [],
    },
  };
}
let sessions = new Map([
  [id, lecture(count)],
  [
    smallId,
    {
      ...lecture(6),
      id: smallId,
      title: "Small lecture",
      transcripts: [],
      chunks: [],
      termInsights: [],
      groupId: null,
    },
  ],
]);
const largeSync = {
  ...lecture(6),
  id: "large-sync",
  title: "Large sync",
  noteMarkdown: "# Large note\n\n" + "長".repeat(200000),
  currentNote: null,
  transcripts: [],
  chunks: [],
  termInsights: [],
};
const cursors = new Map();
let cursorSequence = 0;
function sync(sessionId, cursor) {
  const session =
    sessionId === "large-sync" ? largeSync : sessions.get(sessionId);
  let state = cursors.get(cursor);
  if (!state || state.id !== sessionId) {
    cursor = "cursor-" + ++cursorSequence;
    const { transcripts, chunks, materials, termInsights, terms, ...meta } =
      session;
    const serialized = JSON.stringify(meta);
    const pieces = Array.from(
      { length: Math.ceil(serialized.length / 24000) },
      (_, index) => ({
        kind: "fragment",
        value: {
          kind: "meta",
          key: "meta:" + sessionId,
          index,
          count: Math.ceil(serialized.length / 24000),
          text: serialized.slice(index * 24000, (index + 1) * 24000),
        },
      }),
    );
    state = {
      id: sessionId,
      queue: [
        ...(sessionId === "large-sync"
          ? pieces
          : [{ kind: "meta", value: meta }]),
        ...transcripts.map((value) => ({ kind: "transcript", value })),
        ...chunks.map((value) => ({ kind: "chunk", value })),
        ...termInsights.map((value) => ({ kind: "insight", value })),
      ],
    };
    cursors.set(cursor, state);
  }
  const limit = sessionId === "large-sync" ? 2 : 128;
  return {
    cursor,
    reset: false,
    hasMore: state.queue.length > limit,
    changes: state.queue.splice(0, limit),
  };
}
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
  timezoneId: "UTC",
});
await context.route(/^https:\/\//, (route) => route.abort());
const page = await context.newPage();
page.setDefaultTimeout(12000);
page.on("pageerror", (error) => errors.push(error.message));
const cdp = await context.newCDPSession(page);
await cdp.send("Performance.enable");
const wav = Buffer.alloc(44 + 3200);
wav.write("RIFF");
wav.writeUInt32LE(wav.length - 8, 4);
wav.write("WAVEfmt ", 8);
wav.writeUInt32LE(16, 16);
wav.writeUInt16LE(1, 20);
wav.writeUInt16LE(1, 22);
wav.writeUInt32LE(16000, 24);
wav.writeUInt32LE(32000, 28);
wav.writeUInt16LE(2, 32);
wav.writeUInt16LE(16, 34);
wav.write("data", 36);
wav.writeUInt32LE(3200, 40);
const activity = {
  id: "execution",
  sessionId: id,
  task: "Jev note gate",
  provider: "typesafe",
  model: "fixture-model",
  status: "completed",
  startedAt: createdAt,
  endedAt: createdAt,
  durationMs: 12,
  sourceIds: [key(0)],
  basedOnVersion: 119,
  summary: "wait: pending input retained",
  promptVersion: "note-gate-v1",
  promptHash: "fixture-prompt-hash",
  inputHash: "fixture-input-hash",
  inputBytes: 1200,
  providerLatencyMs: 10,
  queueDelayMs: 1,
  scheduleDelayMs: 8,
  usageJson: '{"output_tokens":3}',
  promptText: "Actual fixture note gate instructions",
};
const agents = [
  { id: "asr", name: "ASR", dependsOn: [] },
  { id: "gate", name: "Jev note gate", dependsOn: ["asr"] },
  { id: "notes", name: "Section notes", dependsOn: ["gate"] },
  { id: "organizer", name: "Section organization", dependsOn: ["notes"] },
].map((a) => ({
  ...a,
  provider: "offline fixture",
  model: "fixture-model",
  status: "disabled",
  promptVersion: "fixture-v1",
  prompt: "Effective fixture prompt",
  trigger: "Changed confirmed input; 10-second decision check",
  lifecycle: "queued -> running -> completed / failed",
  inputRole: "session-only sources",
  outputRole: "versioned section patch",
}));
let audio = [],
  requests = 0,
  payload = 0;
await page.route("**/api/**", async (route) => {
  requests++;
  const request = route.request(),
    url = new URL(request.url()),
    path = url.pathname;
  const sessionId = path.split("/")[3];
  const session = sessions.get(sessionId);
  if (request.method() !== "GET") writes.push(path);
  let json;
  if (path.endsWith("/audio")) {
    audio.push(path.split("/")[3]);
    return route.fulfill({ contentType: "audio/wav", body: wav });
  }
  if (path === "/api/health")
    json = {
      mongo: true,
      gemini: false,
      jev: false,
      sectionNotes: true,
      sessionSync: true,
      aiActivity: true,
      sessionLanguageSettings: true,
      recordingSourceSelection: true,
    };
  else if (path === "/api/capture/status")
    json = live
      ? {
          state: "recording",
          sessionId: id,
          bytes: {},
          capturedThroughMs: 15000,
          recordingElapsedMs: 15000,
          activeSegments: [
            {
              sourceId: "microphone",
              startMs: 16000,
              endMs: 20000,
              recordedAt: createdAt,
              streaming: true,
              interimText: "A live formula continues " + requests,
            },
          ],
        }
      : { state: "idle", bytes: {} };
  else if (path === "/api/sessions")
    json = [...sessions.values()].map(({ id, title, groupId }) => ({
      id,
      title,
      groupId,
    }));
  else if (path === "/api/groups") json = [{ id: group, name: "Study group" }];
  else if (path === `/api/sessions/${sessionId}/sync`)
    json = sync(sessionId, url.searchParams.get("cursor"));
  else if (path.endsWith("/activity"))
    json = sessionId === id ? [activity] : [];
  else if (path.endsWith("/conversations")) json = [];
  else if (path === `/api/sessions/${sessionId}`) json = session;
  else if (path === `/api/groups/${group}/flow`) {
    if (flowFail)
      return route.fulfill({
        status: 503,
        json: { error: "Synthetic flow failure" },
      });
    json = {
      selectedSessionId: id,
      sessions: [{ id, title: "Three-hour lecture fixture" }],
      agents,
      executions: [activity],
      automaticOrganization: false,
      gate: {
        status: "wait",
        decision: "wait",
        inputHash: "saved-input",
        model: "fixture-gate",
        promptVersion: "note-gate-v1",
        waitCount: 2,
        attempts: 0,
        changedAt: createdAt,
        flushRequested: false,
        generationRequested: false,
      },
    };
  } else if (path.endsWith("/notes/history")) {
    const before = +(url.searchParams.get("before") ?? 121);
    const versions = Array.from(
      { length: Math.min(20, before - 1) },
      (_, i) => before - i - 1,
    );
    json = {
      items: versions.map((version) => ({
        version,
        author: "agent",
        createdAt,
      })),
      nextBefore: versions.at(-1) > 1 ? versions.at(-1) : null,
    };
  } else if (/\/notes\/\d+$/.test(path))
    json = {
      version: +path.split("/").at(-1),
      markdown: "## Historical formula\n\nR/n and R/10 remain readable.",
      citations: [],
    };
  else if (path.endsWith("/recovery"))
    json = {
      basedOnVersion: session.noteVersion,
      historicalVersion: 1,
      citations: [],
      candidates: [
        {
          id: "recovered",
          title: "Earlier example",
          markdown: "## Earlier example\n\nR/n; n=10 gives R/10.",
          missingSourceIds: [key(0)],
          blocked: false,
          userEdited: false,
        },
      ],
    };
  else if (path.endsWith("/notes/organize") && request.method() === "POST") {
    edits++;
    json = { version: 121 };
  } else if (path.endsWith("/restore") && request.method() === "POST")
    json = { version: 121 };
  else
    return route.fulfill({
      status: 404,
      json: { error: "Unhandled offline fixture request: " + path },
    });
  payload += Buffer.byteLength(JSON.stringify(json));
  return route.fulfill({ json });
});
async function metric(label) {
  const before = await cdp.send("Performance.getMetrics");
  await cdp.send("HeapProfiler.collectGarbage");
  const after = await cdp.send("Performance.getMetrics"),
    dom = await cdp.send("Memory.getDOMCounters");
  const pick = (result) =>
    Object.fromEntries(result.metrics.map((x) => [x.name, x.value]));
  const a = pick(after),
    b = pick(before);
  const rows = await page.locator("[data-virtual-row]").count(),
    elements = await page.locator("*").count();
  const result = {
    label,
    count,
    mountedRows: rows,
    elements,
    heapBeforeGc: b.JSHeapUsedSize,
    heapAfterGc: a.JSHeapUsedSize,
    taskDuration: a.TaskDuration,
    scriptDuration: a.ScriptDuration,
    layoutDuration: a.LayoutDuration,
    ...dom,
    requests,
    payloadBytes: payload,
    elapsedMs: Date.now() - start,
  };
  measurements.push(result);
  assert(rows < 80, `Too many mounted rows: ${rows}`);
  return result;
}
async function screenshot(name) {
  await page.screenshot({ path: `${folder}/${name}.png` });
}
async function selectLecture(title) {
  await page.getByRole("button", { name: title, exact: true }).click();
  await page
    .locator(".project-name")
    .getByText(title, { exact: true })
    .waitFor();
}
try {
  const testUrl = process.env.PLAYBACK_WEB_TEST_URL ?? "http://127.0.0.1:5174";
  assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(testUrl));
  await page.goto(testUrl);
  await page
    .locator(".note-content")
    .getByText("R/10", { exact: false })
    .first()
    .waitFor();
  // Vite preview serves compiled assets; source-module identity checks require dev.
  if (process.env.PLAYBACK_UI_PRODUCTION !== "yes") {
    const assembly = await page.evaluate(async () => {
      const { readSession } = await import("/src/pages/sessionSync.ts");
      const result = await readSession(
        "large-sync",
        null,
        undefined,
        AbortSignal.timeout(10000),
      );
      const idle = await readSession(
        "large-sync",
        result.session,
        result.cursor,
        AbortSignal.timeout(10000),
      );
      return {
        characters: result.session.noteMarkdown.length,
        sameIdentity: idle.session === result.session,
      };
    });
    assert.equal(assembly.characters, largeSync.noteMarkdown.length);
    assert(assembly.sameIdentity, "Idle sync changed session identity");
  }
  await page.locator(".flowchart svg").waitFor();
  await metric("1350 idle");
  await screenshot("reading-1350");
  assert.equal(
    await page.locator(".note-content .note-ref").count(),
    0,
    "Reading did not hide references",
  );
  const writeCount = writes.length;
  await page.getByRole("button", { name: "Sources", exact: true }).click();
  assert.equal(
    await page.locator(".note-content .note-ref").count(),
    1,
    "Section sources did not aggregate into one entry",
  );
  await page.locator(".note-content .note-ref").click();
  const popup = page.getByRole("dialog", { name: "Grouped audio sources" });
  assert.equal(
    await popup.getByRole("button", { name: /^Jump to/ }).count(),
    3,
    "A time window falsely included uncited sources",
  );
  assert.match(await popup.innerText(), /00:00–00:16/);
  assert.match(await popup.innerText(), /01:20:00–01:20:08/);
  await screenshot("exact-sources");
  await popup.getByRole("button", { name: "Play combined passage" }).click();
  for (let i = 0; i < 80 && !audio.includes(key(1200)); i++)
    await page.waitForTimeout(100);
  assert.deepEqual([...new Set(audio)], [key(0), key(2), key(1200)]);
  await popup
    .getByRole("button", { name: /^Jump to/ })
    .last()
    .click();
  await page.locator(`[id="${key(1200)}"]`).waitFor();
  assert(
    (await page.locator(`[id="${key(1200)}"] details`).getAttribute("open")) !==
      null,
  );
  await metric("1350 late source reveal");
  // Day collapse and revealing an unmounted source must expand the group and mount the target.
  await page.evaluate(
    ({ id, target }) =>
      window.dispatchEvent(
        new CustomEvent("playback-reveal-source", {
          detail: { sessionId: id, id: target },
        }),
      ),
    { id, target: key(0) },
  );
  await page.locator(`[id="${key(0)}"]`).waitFor();
  await page.locator(".timeline-toggle").first().click();
  assert.equal(await page.locator(".transcript-row").count(), 0);
  await page.evaluate(
    ({ id, target }) =>
      window.dispatchEvent(
        new CustomEvent("playback-reveal-source", {
          detail: { sessionId: id, id: target },
        }),
      ),
    { id, target: key(1200) },
  );
  await page.locator(`[id="${key(1200)}"]`).waitFor();
  await page.getByRole("button", { name: "Reading", exact: true }).click();
  await page
    .locator(".note-content")
    .getByRole("button", { name: "Explain bottleneck", exact: true })
    .first()
    .hover();
  const explanation = page.getByRole("dialog", {
    name: "bottleneck explanation",
  });
  await explanation
    .getByText("The resource that limits throughput.", { exact: true })
    .waitFor();
  await explanation
    .getByRole("button", { name: "Read full explanation" })
    .click();
  await explanation.getByText(/equal-share target/).waitFor();
  await screenshot("expanded-explanation");
  await explanation.getByRole("button", { name: "Close explanation" }).click();
  assert.equal(
    writes.length,
    writeCount,
    "Reading, source popup or explanation hover generated a request",
  );
  await page.getByRole("button", { name: "History & recovery" }).click();
  const history = page.getByRole("dialog", { name: "Note history & recovery" });
  await history
    .getByRole("button", { name: "v120 · agent", exact: true })
    .waitFor();
  for (let i = 0; i < 5; i++) {
    await history.getByRole("button", { name: "Load older versions" }).click();
    await page.waitForTimeout(60);
  }
  await history
    .getByRole("button", { name: "v20 · agent", exact: true })
    .waitFor();
  await history.getByLabel("Go to version").fill("1");
  await history
    .getByRole("button", { name: "Read version", exact: true })
    .click();
  await history
    .locator("pre")
    .getByText(/R\/n and R\/10/)
    .waitFor();
  await history
    .getByRole("button", { name: "Preview recovery into current notes" })
    .click();
  await history.getByText("Merge v1 into v120").waitFor();
  await screenshot("recovery-preview");
  assert.equal(writes.length, writeCount, "Recovery preview wrote to notes");
  await history.getByRole("button", { name: "Close note history" }).click();
  await page.getByLabel("Group options for Study group").click();
  await page.getByRole("button", { name: "View AI flow" }).click();
  const flow = page.getByRole("dialog", { name: "Study group · AI flow" });
  await flow.getByText("Configured pipeline", { exact: true }).waitFor();
  await flow.locator(".flowchart svg").waitFor();
  await flow
    .getByText("Saved note gate · wait · wait", { exact: true })
    .click();
  await flow
    .getByText("Input identity: saved-input", { exact: true })
    .waitFor();
  await flow
    .locator(".flow-execution > summary")
    .filter({ hasText: "Jev note gate" })
    .click();
  await flow.getByText("Prompt note-gate-v1", { exact: false }).waitFor();
  assert(
    await flow.getByRole("button", { name: "Close AI flow" }).isVisible(),
    "Scrolling execution details hid the modal close control",
  );
  assert.match(await flow.innerText(), /Provider: 10 ms/);
  await screenshot("group-flow");
  const bounds = await flow.boundingBox();
  assert(
    bounds.width > 950 && bounds.width < 1080,
    "Flow modal width is not about 70vw",
  );
  await page.keyboard.press("Escape");
  await flow.waitFor({ state: "hidden" });
  assert.equal(
    writes.length,
    writeCount,
    "Reading flow generated a paid request",
  );
  flowFail = true;
  await page.getByLabel("Group options for Study group").click();
  await page.getByRole("button", { name: "View AI flow" }).click();
  await flow.getByRole("alert").waitFor();
  flowFail = false;
  await flow.getByRole("button", { name: "Retry" }).click();
  await flow.getByText("Configured pipeline", { exact: true }).waitFor();
  await flow.getByRole("button", { name: "Close AI flow" }).click();
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await page
    .getByLabel("Editable Markdown")
    .fill("Unsaved formula R/n and R/10");
  await page.locator("#organize-section").selectOption("fair-sharing");
  assert(
    await page
      .getByRole("button", { name: "Organize section", exact: true })
      .isDisabled(),
  );
  await page.getByLabel("Editable Markdown").evaluate((e) => {
    e.focus();
    e.setSelectionRange(8, 8);
  });
  live = true;
  await page.waitForTimeout(4500);
  assert.equal(
    await page.getByLabel("Editable Markdown").inputValue(),
    "Unsaved formula R/n and R/10",
  );
  assert.equal(
    await page
      .getByLabel("Editable Markdown")
      .evaluate((e) => e.selectionStart),
    8,
    "Background refresh moved the caret",
  );
  const changed = sessions.get(id);
  changed.noteVersion++;
  changed.noteMarkdown += "\n\n## Later topic\n\nLater source-backed topic.";
  const {
    transcripts: omittedTranscripts,
    chunks: omittedChunks,
    materials: omittedMaterials,
    terms: omittedTerms,
    termInsights: omittedInsights,
    ...changedMeta
  } = changed;
  for (const state of cursors.values())
    if (state.id === id) state.queue.push({ kind: "meta", value: changedMeta });
  await page
    .getByRole("alert")
    .getByText(/Saved notes changed while you were editing/)
    .waitFor();
  assert.equal(
    await page.getByLabel("Editable Markdown").inputValue(),
    "Unsaved formula R/n and R/10",
  );
  assert.equal(
    await page
      .getByLabel("Editable Markdown")
      .evaluate((e) => e.selectionStart),
    8,
    "A concurrent saved version moved the caret",
  );
  assert(
    await page.getByRole("button", { name: "Save", exact: true }).isDisabled(),
  );
  assert(
    await page
      .getByRole("button", { name: "Revise with AI", exact: true })
      .isDisabled(),
  );
  await page
    .getByRole("button", {
      name: "Load current notes and keep my draft for recovery",
    })
    .click();
  assert.match(
    await page.getByLabel("Editable Markdown").inputValue(),
    /Later source-backed topic/,
  );
  await page.getByRole("button", { name: "Recover my previous draft" }).click();
  assert.equal(
    await page.getByLabel("Editable Markdown").inputValue(),
    "Unsaved formula R/n and R/10",
  );
  assert(
    await page.getByRole("button", { name: "Save", exact: true }).isDisabled(),
    "Recovering the old draft silently advanced its save base",
  );
  await metric("1350 live interim + unsaved edit");
  live = false;
  // Load a three-hour, mic + system fixture; both UI and API retain 2,700 raw identities.
  count = 2700;
  sessions.set(id, lecture(count));
  cursors.clear();
  await selectLecture("Small lecture");
  await selectLecture("Three-hour lecture fixture");
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  await page.getByLabel("Combine completed passages").uncheck();
  await page.locator(".virtual-transcript[data-total-rows='2704']").waitFor();
  assert.equal(
    await page.locator(".virtual-transcript").getAttribute("data-total-rows"),
    "2704",
  );
  await page.getByLabel("Combine completed passages").check();
  assert(
    Number(
      await page.locator(".virtual-transcript").getAttribute("data-total-rows"),
    ) < 2704,
    "Completed transcripts did not consolidate",
  );
  await page.locator(".flowchart svg").waitFor();
  await metric("2700 warmed idle");
  await screenshot("reading-2700");
  for (let cycle = 0; cycle < 8; cycle++) {
    await page.getByRole("button", { name: "Edit", exact: true }).click();
    const editor = page.getByLabel("Editable Markdown");
    const value = sessions.get(id).noteMarkdown;
    for (let update = 0; update < 12; update++)
      await editor.fill(value + `\n\nEditor iteration ${cycle}:${update}.`);
    await editor.fill(value);
    await page.getByRole("button", { name: "Preview", exact: true }).click();
    await page.locator(".flowchart svg").waitFor();
    await page.getByRole("button", { name: "Sources", exact: true }).click();
    await page.locator(".note-content .note-ref").click();
    await popup.getByRole("button", { name: "Close sources" }).click();
    await page.getByRole("button", { name: "Reading", exact: true }).click();
    await metric(`2700 edit/preview cycle ${cycle}`);
  }
  await page.evaluate(() => {
    document.documentElement.style.zoom = "1.25";
  });
  await screenshot("zoom-125");
  await metric("2700 zoom125");
  await page.evaluate(() => {
    document.documentElement.style.zoom = "";
  });
  await page.setViewportSize({ width: 760, height: 900 });
  await screenshot("narrow");
  await metric("2700 narrow");
  await page.setViewportSize({ width: 1440, height: 1000 });
  await selectLecture("Small lecture");
  await page.waitForTimeout(600);
  await metric("return to small session");
  assert.equal(await page.locator(".transcript-row").count(), 0);
  assert.equal(await page.locator("[data-virtual-row]").count(), 0);
  assert.equal(
    writes.length,
    writeCount,
    "Passive QA accidentally called a generation endpoint",
  );
  assert.deepEqual(errors, []);
  await writeFile(
    `${folder}/metrics.json`,
    JSON.stringify(
      {
        browser: await browser.version(),
        fixture:
          "1350/2700 mic+system source rows; 8 edit-preview cycles, 96 edits",
        durationMs: Date.now() - start,
        writes,
        measurements,
        limitations:
          "Fixture UI and browser allocation/retention checks; not natural lecture generation quality or hours-long leak proof.",
      },
      null,
      2,
    ),
  );
  console.log(
    JSON.stringify({
      passed: true,
      durationMs: Date.now() - start,
      measurements: measurements.map((x) => ({
        label: x.label,
        rows: x.mountedRows,
        dom: x.elements,
        heapMiB: +(x.heapAfterGc / 1048576).toFixed(2),
      })),
      writes,
    }),
  );
} catch (error) {
  await screenshot("failure");
  console.error(await page.locator("body").innerText());
  throw error;
} finally {
  await context.close();
  await browser.close();
}
