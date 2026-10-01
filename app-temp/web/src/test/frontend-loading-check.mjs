// Cold production asset loading plus first diagram, with offline API fixtures.
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { chromium } from "playwright-core";
const url = process.env.LOADING_BASE_URL ?? "http://127.0.0.1:5174";
const label = process.env.LOADING_RUN_LABEL ?? "current";
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(url));
assert(/^[a-z0-9-]+$/.test(label));
const folder = `output/playwright/frontend-loading/${label}`;
await mkdir(folder, { recursive: true });
const id = "3".repeat(32),
  errors = [],
  writes = [];
const session = {
  id,
  title: "Cold loading lecture",
  createdAt: "2026-09-29T00:00:00Z",
  groupId: null,
  noteMarkdown:
    "## Lecture notes\n\nPlain notes should be readable before a diagram is needed.",
  noteVersion: 1,
  noteLanguage: "en",
  translationEnabled: false,
  translationLanguage: "zh-Hant",
  transcripts: [],
  chunks: [],
  terms: [],
  termInsights: [],
  materials: [],
  currentNote: null,
  sourceGroups: [],
};
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
});
await context.route(
  (u) => u.protocol === "https:" || u.hostname !== "127.0.0.1",
  (route) => route.abort(),
);
let rendererFailures = 0;
if (process.env.LOADING_RENDERER_FAILURE === "yes")
  await context.route("**/assets/mermaid.core-*.js", (route) => {
    if (rendererFailures++ === 0) return route.abort();
    return route.continue();
  });
const page = await context.newPage();
page.on("pageerror", (e) => errors.push(e.message));
await page.route("**/api/**", async (route) => {
  const request = route.request(),
    path = new URL(request.url()).pathname;
  if (request.method() !== "GET") {
    writes.push(path);
    return route.abort();
  }
  const json =
    path === "/api/health"
      ? {
          mongo: true,
          gemini: false,
          jev: false,
          recordingSourceSelection: true,
        }
      : path === "/api/capture/status"
        ? {
            state: "idle",
            bytes: {},
            levels: {},
            autoAsr: false,
            noSoundWarning: false,
            capturedThroughMs: 0,
            lastFinalizedAtMs: 0,
            activeSegments: [],
          }
        : path === "/api/sessions"
          ? [{ id, title: session.title, groupId: null }]
          : path === `/api/sessions/${id}`
            ? session
            : [];
  await route.fulfill({ json });
});
const cdp = await context.newCDPSession(page);
await cdp.send("Network.enable");
await cdp.send("Network.setCacheDisabled", { cacheDisabled: true });
const network = {
  latency: 80,
  downloadThroughput: (4 * 1024 * 1024) / 8,
  uploadThroughput: (1024 * 1024) / 8,
};
await cdp.send("Network.emulateNetworkConditions", {
  offline: false,
  ...network,
  connectionType: "cellular4g",
});
await cdp.send("Performance.enable");
const resources = () =>
  page.evaluate(() =>
    performance
      .getEntriesByType("resource")
      .filter((x) => x.name.endsWith(".js"))
      .map((x) => ({
        name: new URL(x.name).pathname,
        bytes: x.encodedBodySize,
        transferBytes: x.transferSize,
        durationMs: x.duration,
      })),
  );
try {
  await page.goto(url);
  await page
    .locator(".project-name")
    .getByText(session.title, { exact: true })
    .waitFor();
  await page.locator(".workspace-loading").waitFor({ state: "hidden" });
  const startup = await page.evaluate(() => ({
    appReadyMs: performance.now(),
    fcpMs: performance.getEntriesByName("first-contentful-paint")[0]?.startTime,
    domContentLoadedMs:
      performance.getEntriesByType("navigation")[0]?.domContentLoadedEventEnd,
  }));
  const initialJs = await resources();
  await page.screenshot({ path: `${folder}/plain-notes.png` });
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  const diagramNote =
    session.noteMarkdown +
    '\n\n```mermaid\nflowchart LR\n A["Source"] --> B["Notes"]\n```\n';
  await page.getByLabel("Editable Markdown").fill(diagramNote);
  const diagramStart = Date.now();
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  if (process.env.LOADING_RENDERER_FAILURE === "yes") {
    await page
      .getByRole("alert")
      .getByText(
        "Diagram renderer could not load. Save or copy your draft, then reload to retry.",
        { exact: true },
      )
      .waitFor();
    const alertBounds = await page
      .locator(".flowchart > p[role='alert']")
      .evaluate((el) => ({ client: el.clientWidth, scroll: el.scrollWidth }));
    assert(
      alertBounds.scroll <= alertBounds.client + 1,
      "Diagram load failure text is clipped",
    );
    await page.screenshot({ path: `${folder}/renderer-load-failed.png` });
    // Browsers cache a rejected dynamic module within the document. Reload is
    // required; the UI explicitly tells readers to preserve an unsaved draft first.
    await page.getByRole("button", { name: "Edit", exact: true }).click();
    assert.equal(
      await page.getByLabel("Editable Markdown").inputValue(),
      diagramNote,
    );
    await page.reload();
    await page.locator(".workspace-loading").waitFor({ state: "hidden" });
    await page.getByRole("button", { name: "Edit", exact: true }).click();
    await page.getByLabel("Editable Markdown").fill(diagramNote);
    await page.getByRole("button", { name: "Preview", exact: true }).click();
  }
  await page
    .locator(".note-content .flowchart svg")
    .waitFor({ timeout: 30000 });
  const firstDiagramMs = Date.now() - diagramStart;
  const afterDiagramJs = await resources();
  const diagramOnlyJs = afterDiagramJs.filter(
    (x) => !initialJs.some((prior) => prior.name === x.name),
  );
  if (process.env.LOADING_EXPECT_LAZY === "yes") {
    assert(
      initialJs.every((x) => !x.name.includes("mermaid")),
      "Plain notes loaded Mermaid eagerly",
    );
    assert(
      diagramOnlyJs.some((x) => x.name.includes("mermaid")),
      "First diagram did not load its renderer on demand",
    );
  }
  await page.screenshot({ path: `${folder}/first-diagram.png` });
  assert.deepEqual(errors, []);
  assert.deepEqual(writes, []);
  const metrics = Object.fromEntries(
    (await cdp.send("Performance.getMetrics")).metrics.map((x) => [
      x.name,
      x.value,
    ]),
  );
  const result = {
    browser: await browser.version(),
    evidence:
      "Cold production browser; offline immediate API fixture; cache disabled; simulated 4 Mbps / 80 ms asset network",
    url,
    network,
    startup,
    firstDiagramMs,
    initialJs,
    initialJsBytes: initialJs.reduce((sum, x) => sum + x.bytes, 0),
    diagramOnlyJs,
    rendererFailures,
    metrics,
    errors,
    writes,
    limitations:
      "One local sample during the long soak. Asset latency is simulated; no real DB/provider/network service latency or whole-browser RSS measurement.",
  };
  await writeFile(`${folder}/results.json`, JSON.stringify(result, null, 2));
  console.log(
    JSON.stringify({
      label,
      ...startup,
      initialJsBytes: result.initialJsBytes,
      firstDiagramMs,
      diagramOnlyChunks: diagramOnlyJs.length,
    }),
  );
} finally {
  await context.close();
  await browser.close();
}
