// Offline production-browser rendering and retention check. No DB/provider writes.
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import { chromium } from "playwright-core";

const url = process.env.MATH_BASE_URL ?? "http://127.0.0.1:5180";
const label = process.env.MATH_RUN_LABEL ?? "current";
const baseline = process.env.MATH_BASELINE === "yes";
const cycles = Number(process.env.MATH_CYCLES ?? 80);
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(url));
assert(/^[a-z0-9-]+$/.test(label));
assert(Number.isInteger(cycles) && cycles >= 10 && cycles <= 300);
const folder = `output/playwright/markdown-math/${label}`;
await mkdir(folder, { recursive: true });
const id = "4".repeat(32),
  smallId = "5".repeat(32),
  createdAt = "2026-09-29T00:00:00Z";
const citation = "cite_" + "a".repeat(20),
  errors = [],
  writes = [],
  metrics = [];
const transcripts = Array.from({ length: 2700 }, (_, i) => ({
  id: `math-source-${i}`,
  sourceId: i % 2 ? "system" : "microphone",
  startMs: Math.floor(i / 2) * 8000,
  endMs: Math.floor(i / 2) * 8000 + 8000,
  recordedAt: new Date(
    Date.parse(createdAt) + Math.floor(i / 2) * 8000,
  ).toISOString(),
  original:
    "Fermat's little theorem requires a prime modulus; its converse does not establish primality.",
  uncertain: false,
  noteStatus: "completed",
}));
const session = {
  id,
  title: "Math lecture",
  groupId: null,
  createdAt,
  noteVersion: 18,
  noteLanguage: "en",
  noteMarkdown:
    "## Lecture notes\n\nPlain Markdown should load without either drawing engine.",
  translationEnabled: false,
  translationLanguage: "en",
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
  currentNote: null,
  sourceGroups: [
    {
      id: citation,
      sourceId: "microphone",
      startMs: 0,
      endMs: 8000,
      recordedAt: createdAt,
      transcriptIds: [transcripts[0].id, transcripts[1].id],
    },
  ],
};
const small = {
  ...session,
  id: smallId,
  title: "Small lecture",
  noteMarkdown: "## Small note\n\nOnly plain text remains.",
  noteVersion: 1,
  transcripts: [],
  chunks: [],
  sourceGroups: [],
};
const formulaNote = String.raw`## Fermat's Little Theorem and Primality Testing

If $p$ is prime, then for any integer $a$ with $1 \le a < p$, the condition $a^{p-1} \equiv 1 \pmod{p}$ holds. [CITATION]

If $a^{n-1} \not\equiv 1 \pmod{n}$, then $n$ is not prime. The converse does not prove that $n$ is prime.

$$
\sum_{i=1}^{n} i = \frac{n(n+1)}{2}
$$

| Expression | Meaning |
| --- | --- |
| $\frac{a+b}{c}$ | A fraction in a table |
| $\begin{pmatrix}1&2\\3&4\end{pmatrix}$ | Matrix |

Literal code: \`$p$\`. Currency: \$5 and \$10.
`
  .replace("CITATION", citation)
  .replaceAll("\\`", "`");
const diagram =
  '\n```mermaid\nflowchart LR\n A["Prime modulus p"] --> B["Fermat condition"]\n B --> C["Test can disprove primality"]\n```\n';
const longNote = Array.from({ length: 20 }, (_, i) =>
  formulaNote.replace(
    "## Fermat's Little Theorem and Primality Testing",
    `## Theorem and example ${i + 1}`,
  ),
).join("\n\n");

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
const page = await context.newPage();
page.setDefaultTimeout(20000);
page.on("pageerror", (e) => errors.push(e.message));
await context.route("**/api/**", async (route) => {
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
          ? [
              { id, title: session.title, groupId: null },
              { id: smallId, title: small.title, groupId: null },
            ]
          : path === `/api/sessions/${id}`
            ? session
            : path === `/api/sessions/${smallId}`
              ? small
              : [];
  await route.fulfill({ json });
});
const cdp = await context.newCDPSession(page);
await cdp.send("Performance.enable");
const values = (result) =>
  Object.fromEntries(result.metrics.map((x) => [x.name, x.value]));
async function metric(phase) {
  const before = values(await cdp.send("Performance.getMetrics"));
  await cdp.send("HeapProfiler.collectGarbage");
  const after = values(await cdp.send("Performance.getMetrics"));
  const row = {
    phase,
    heapBefore: before.JSHeapUsedSize,
    heapAfter: after.JSHeapUsedSize,
    scriptDuration: after.ScriptDuration,
    taskDuration: after.TaskDuration,
    layoutDuration: after.LayoutDuration,
    ...(await cdp.send("Memory.getDOMCounters")),
    elements: await page.locator("*").count(),
    formulas: await page.locator("math").count(),
    temporaryDiagramHosts: await page
      .locator("body > [data-diagram-render-host]")
      .count(),
  };
  metrics.push(row);
  console.log(
    JSON.stringify({
      phase,
      heapMiB: +(row.heapAfter / 1048576).toFixed(2),
      elements: row.elements,
      formulas: row.formulas,
    }),
  );
  assert.equal(row.temporaryDiagramHosts, 0);
  return row;
}
const resources = () =>
  page.evaluate(() =>
    performance
      .getEntriesByType("resource")
      .filter((x) => new URL(x.name).pathname.endsWith(".js"))
      .map((x) => ({
        name: new URL(x.name).pathname,
        bytes: x.encodedBodySize,
      })),
  );
async function select(title) {
  await page.getByRole("button", { name: title, exact: true }).click();
  await page
    .getByRole("banner")
    .getByRole("heading", { level: 1 })
    .getByText(title, { exact: true })
    .waitFor();
  await page
    .getByRole("status")
    .filter({ hasText: "Loading session…" })
    .waitFor({ state: "hidden" });
}
async function edit(text) {
  await page.getByRole("button", { name: "Edit", exact: true }).click();
  await page.getByLabel("Editable Markdown").fill(text);
  await page.getByRole("button", { name: "Preview", exact: true }).click();
  if (!baseline && text.includes("$p$"))
    await page.getByTestId("note-content").locator("math").first().waitFor();
}
async function screenshot(name) {
  await page
    .locator("details", {
      has: page.getByRole("navigation", { name: "Note topics" }),
    })
    .evaluateAll((nodes) =>
      nodes.forEach((node) => {
        node.open = false;
      }),
    );
  await page.getByTestId("notes-body").evaluate((el) => {
    el.scrollTop = 0;
  });
  await page.screenshot({ path: `${folder}/${name}.png` });
}
try {
  await page.goto(url);
  await page
    .getByRole("banner")
    .getByRole("heading", { level: 1 })
    .getByText(session.title, { exact: true })
    .waitFor();
  await page
    .getByRole("status")
    .filter({ hasText: "Loading session…" })
    .waitFor({ state: "hidden" });
  const plainAssets = await resources();
  assert(
    plainAssets.every((x) => !/MathFormula|mermaid|katex/i.test(x.name)),
    "Plain notes loaded a drawing engine eagerly",
  );
  await metric("plain-startup");
  await edit(formulaNote);
  if (!baseline) {
    assert.equal(
      await page.getByTestId("note-content").locator("math").count(),
      10,
      "Screenshot equations were not rendered",
    );
    assert(
      (await page.getByTestId("note-content").locator("mfrac").count()) >= 2,
      "Fractions are missing",
    );
    assert(
      (await page.getByTestId("note-content").locator("mtable").count()) >= 1,
      "Matrix is missing",
    );
    assert.equal(
      await page.getByTestId("note-content").locator("table math").count(),
      2,
    );
    assert(
      await page
        .getByTestId("note-content")
        .locator("code")
        .filter({ hasText: "$p$" })
        .count(),
      "Code was interpreted as math",
    );
    assert(
      (await page.getByTestId("note-content").innerText()).includes(
        "$5 and $10",
      ),
      "Escaped currency was interpreted as math",
    );
    const mathAssets = await resources();
    assert(
      mathAssets.every((x) => !/mermaid/i.test(x.name)),
      "Equations loaded the flowchart engine",
    );
    await page.getByRole("button", { name: "Edit", exact: true }).click();
    assert.equal(
      await page.getByLabel("Editable Markdown").inputValue(),
      formulaNote,
      "Rendering changed the source note",
    );
    await page.getByRole("button", { name: "Preview", exact: true }).click();
  }
  await screenshot("equations-desktop");
  await edit(formulaNote + diagram);
  await page
    .getByTestId("note-content")
    .getByLabel("Rendered flowchart")
    .locator("svg")
    .waitFor({ timeout: 30000 });
  await page.getByRole("button", { name: "Sources", exact: true }).click();
  await page
    .getByTestId("note-content")
    .getByRole("button", { name: /^Open audio sources/ })
    .first()
    .click();
  await page.getByRole("dialog", { name: "Grouped audio sources" }).waitFor();
  await page.getByLabel("Close sources", { exact: true }).click();
  await page.getByRole("button", { name: "Reading", exact: true }).click();
  await screenshot("equations-flowchart");
  await page.evaluate(() => {
    document.documentElement.style.zoom = "1.25";
  });
  await screenshot("equations-125-percent");
  await page.evaluate(() => {
    document.documentElement.style.zoom = "";
  });
  await page.setViewportSize({ width: 375, height: 812 });
  await page.getByRole("button", { name: "Notes", exact: true }).click();
  await screenshot("equations-mobile");
  const fit = await page.evaluate(() => ({
    width: innerWidth,
    pageWidth: document.documentElement.scrollWidth,
    notesWidth: document.querySelector('[data-testid="notes-body"]')
      .clientWidth,
    notesScrollWidth: document.querySelector('[data-testid="notes-body"]')
      .scrollWidth,
  }));
  assert(
    fit.pageWidth <= fit.width + 1 &&
      fit.notesScrollWidth <= fit.notesWidth + 1,
    "Equations make the page overflow horizontally",
  );
  await page.setViewportSize({ width: 1440, height: 1000 });
  await select(small.title);
  const warmSmall = await metric("small-after-engines-warmed");
  await select(session.title);
  await edit(longNote + diagram);
  await page
    .getByTestId("note-content")
    .getByLabel("Rendered flowchart")
    .locator("svg")
    .waitFor();
  const warmLong = await metric("long-warm");
  const soakStart = Date.now();
  for (let i = 1; i <= cycles; i++) {
    await edit(longNote + diagram + `\n\nUpdated example $x = ${i}$`);
    await page
      .getByTestId("note-content")
      .getByLabel("Rendered flowchart")
      .locator("svg")
      .waitFor();
    if (i % 10 === 0) await metric(`edit-${i}`);
    if (i % 20 === 0) {
      await select(small.title);
      await select(session.title);
    }
  }
  await select(small.title);
  await page.waitForTimeout(1000);
  const finalSmall = await metric("small-after-editing");
  assert.equal(
    await page.getByTestId("formula").count(),
    0,
    "Previous-session formulas remain mounted",
  );
  assert(
    finalSmall.heapAfter - warmSmall.heapAfter < 5 * 1048576,
    "Retained heap grew by more than 5 MiB after returning to the same small session",
  );
  const periodic = metrics.filter((x) => x.phase.startsWith("edit-"));
  assert(
    Math.max(...periodic.map((x) => x.heapAfter)) - warmLong.heapAfter <
      8 * 1048576,
    "Collected heap grows with edit cycles",
  );
  if (!baseline) {
    await select(session.title);
    await edit(
      String.raw`## Incomplete formula

$\frac{1}{$

$$
` +
        "x+".repeat(2500) +
        "\n$$",
    );
    await page.getByTestId("formula").locator("code").first().waitFor();
    assert.equal(
      await page.getByTestId("note-content").locator("math").count(),
      0,
      "Invalid or oversized math should preserve source text",
    );
    assert.equal(await page.getByTestId("formula").locator("code").count(), 2);
    await screenshot("invalid-math-source-preserved");
    await edit("```math\n\\frac{1}{2}\n```\n\n```text\n$literal$\n```");
    await page.getByTestId("note-content").locator("math").waitFor();
    assert.equal(
      await page
        .getByTestId("note-content")
        .locator('math[display="block"]')
        .count(),
      1,
      "Math fences should render a block equation",
    );
    assert.equal(
      await page.getByTestId("note-content").locator("pre code").textContent(),
      "$literal$\n",
      "Normal code fences must remain literal",
    );
    await edit(String.raw`$\def\loop{\loop}\loop$`);
    await page.getByTestId("formula").locator("code").waitFor();
    assert.equal(
      await page.getByTestId("note-content").locator("math").count(),
      0,
      "Recursive macros must stop and preserve source",
    );
    await edit(String.raw`$\href{https://example.com/math}{x}$`);
    await page.getByTestId("formula").waitFor();
    assert.equal(
      await page
        .getByTestId("formula")
        .locator("[href], [src], script")
        .count(),
      0,
      "Formula markup must not create external content",
    );
    const unavailable = await context.newPage();
    unavailable.on("pageerror", (e) => errors.push(e.message));
    await unavailable.route("**/assets/MathFormula-*.js", (route) =>
      route.abort(),
    );
    await unavailable.goto(url);
    await unavailable
      .getByRole("button", { name: "Edit", exact: true })
      .click();
    await unavailable.getByLabel("Editable Markdown").fill("Equation $p^{2}$");
    await unavailable
      .getByRole("button", { name: "Preview", exact: true })
      .click();
    await unavailable
      .getByTestId("formula")
      .locator('code[title*="could not load"]')
      .waitFor();
    assert.equal(
      await unavailable.getByTestId("formula").locator("code").textContent(),
      "p^{2}",
      "A failed lazy import must preserve the equation source",
    );
    await unavailable.close();
  }
  assert.deepEqual(errors, []);
  assert.deepEqual(writes, []);
  const result = {
    browser: await browser.version(),
    baseline,
    cycles,
    fixtureTranscriptCount: transcripts.length,
    fixtureDataHours: 3,
    sections: 20,
    realSoakSeconds: (Date.now() - soakStart) / 1000,
    metrics,
    fit,
    plainAssets,
    assetsAfterMathAndFlow: await resources(),
    smallHeapGrowthMiB: (finalSmall.heapAfter - warmSmall.heapAfter) / 1048576,
    errors,
    writes,
    limitations:
      "Offline production-browser fixture, JS heap after explicit GC. Not live providers, whole-browser RSS or a three-hour wall-clock soak.",
  };
  await writeFile(`${folder}/results.json`, JSON.stringify(result, null, 2));
  console.log(
    JSON.stringify({
      result: `${folder}/results.json`,
      cycles,
      smallHeapGrowthMiB: result.smallHeapGrowthMiB,
    }),
  );
} catch (error) {
  await writeFile(`${folder}/failure.txt`, String(error.stack));
  await page.screenshot({ path: `${folder}/failure.png` });
  throw error;
} finally {
  await context.close();
  await browser.close();
}
