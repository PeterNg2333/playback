import assert from "node:assert/strict";
import { readFile, writeFile, mkdir } from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright-core";
const folder = path.resolve("app-temp/data/validation/runs/2026-09-29-week3");
const phaseIndex = process.argv.indexOf("--phase");
const phase = phaseIndex < 0 ? "read" : process.argv[phaseIndex + 1];
if (!process.argv.includes("--live")) {
  console.log(
    "Offline default: saved Week 3 results only. --live explicitly enables a bounded browser phase.",
  );
  process.exit(0);
}
assert(
  [
    "notes",
    "organize",
    "chat",
    "chat-read",
    "web",
    "web-read",
    "read",
    "restart",
  ].includes(phase),
);
const readChat = ["chat-read", "web-read"].includes(phase);
await mkdir(folder, { recursive: true });
const resultFile = path.join(folder, `browser-${phase}.json`);
try {
  const cached = JSON.parse(await readFile(resultFile, "utf8"));
  if (!process.argv.includes("--refresh")) {
    console.log(
      `Saved ${phase}: ${cached.passed ? "passed" : "failed"}; no new calls.`,
    );
    process.exit(cached.passed ? 0 : 2);
  }
} catch (error) {
  if (error.code !== "ENOENT") throw error;
}
const fixture = JSON.parse(
  await readFile(path.join(folder, "db-session.json"), "utf8"),
);
const initial = JSON.parse(
  await readFile(path.join(folder, "provider-results.json"), "utf8"),
);
const api = "http://127.0.0.1:5081/api",
  web = "http://127.0.0.1:5177";
const get = async (endpoint) => {
  const r = await fetch(api + endpoint);
  assert.equal(r.status, 200, endpoint);
  return r.json();
};
const health = await get("/health");
assert.equal(health.mongo, true);
assert.equal(health.database, "playback_e2e");
assert.equal(health.autoNotes, false);
assert.equal(health.autoTerms, false);
assert.equal(health.asrPaused, true);
const view = () => get(`/sessions/${fixture.id}`);
const oldAttempts = initial.records.filter(
  (x) =>
    ["Note revision", "Section organization"].includes(x.task) && x.usageJson,
).length;
async function remaining() {
  const records = await get(`/sessions/${fixture.id}/activity`);
  return (
    9 - oldAttempts - records.filter((x) => x.task === "Note revision").length
  );
}
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
});
const page = await context.newPage();
page.setDefaultTimeout(30000);
const errors = [],
  calls = [],
  snapshots = [];
let passed = false,
  failure;
page.on("pageerror", (error) => errors.push(error.message));
page.on("request", (request) => {
  if (request.method() === "POST" && request.url().startsWith(web + "/api/"))
    calls.push({
      endpoint: new URL(request.url()).pathname,
      request: request.postData(),
    });
});
await context.route(/^https:\/\//, (route) => route.abort());
await page.addInitScript((id) => {
  if (window.top === window) localStorage.setItem("playback-session", id);
}, fixture.id);
const screenshot = (name) =>
  page.screenshot({ path: path.join(folder, `${phase}-${name}.png`) });
async function save() {
  await writeFile(
    resultFile,
    JSON.stringify(
      {
        testedAt: new Date().toISOString(),
        phase,
        passed,
        failure,
        health,
        sessionId: fixture.id,
        calls,
        snapshots,
        errors,
        noteAttemptsRemaining: await remaining().catch(() => null),
        evidence:
          "Production browser -> real API -> MongoDB; provider requests only in explicit notes/organize/chat phases; no audio.",
      },
      null,
      2,
    ),
  );
}
try {
  await page.goto(web);
  await page
    .getByRole("heading", { name: fixture.title, exact: true })
    .waitFor();
  let current = await view();
  assert.equal(current.transcripts.length, 137);
  assert.equal(current.chunks.length, 0);
  await page
    .getByRole("status")
    .getByText("Sources awaiting revision", { exact: true })
    .waitFor();
  if (phase === "notes") {
    const marker = process.argv.indexOf("--max-calls");
    const maxCalls = marker < 0 ? 5 : Number(process.argv[marker + 1]);
    assert(Number.isInteger(maxCalls) && maxCalls >= 1 && maxCalls <= 5);
    for (
      let i = 0;
      i < maxCalls &&
      current.transcripts.some(
        (x) => !["completed", "deferred", "suppressed"].includes(x.noteStatus),
      );
      i++
    ) {
      assert((await remaining()) >= 1, "Nine-note-generation budget reached");
      const before = current;
      const response = page.waitForResponse(
        (r) =>
          r.url().endsWith(`/sessions/${fixture.id}/notes/generate`) &&
          r.request().method() === "POST",
        { timeout: 140000 },
      );
      await page
        .getByRole("button", { name: "Revise with AI", exact: true })
        .click();
      await page.getByTestId("note-ai-status").waitFor();
      assert.equal(
        await page
          .getByRole("button", { name: "Save", exact: true })
          .isDisabled(),
        true,
      );
      if (i === 0) await screenshot("loading");
      const result = await response;
      const body = await result.json();
      snapshots.push({
        batch: i + 1,
        httpStatus: result.status(),
        result: body,
      });
      await save();
      assert.equal(
        result.status(),
        200,
        body.error ?? "Note generation failed",
      );
      current = await view();
      assert(current.noteVersion > before.noteVersion, "No note version saved");
      for (const point of before.currentNote?.sections?.flatMap(
        (x) => x.points,
      ) ?? [])
        assert(
          current.noteMarkdown.includes(point.text),
          "Earlier written point disappeared",
        );
      await writeFile(
        path.join(folder, `db-note-v${current.noteVersion}.json`),
        JSON.stringify(current.currentNote, null, 2),
      );
      await page
        .getByRole("button", { name: "Revise with AI", exact: true })
        .waitFor({ state: "visible" });
      await page.waitForFunction(
        (version) =>
          document
            .querySelector('[data-testid="notes-panel"] h2 + span')
            ?.textContent.includes("v" + version),
        current.noteVersion,
      );
      console.log(
        `Browser notes v${current.noteVersion}: saved; ${current.transcripts.filter((x) => x.noteStatus === "pending").length} pending sources.`,
      );
    }
    assert(
      !current.transcripts.some(
        (x) => !["completed", "deferred", "suppressed"].includes(x.noteStatus),
      ),
      "Some hour sources remain unprocessed at the bounded limit",
    );
  }
  if (phase === "organize") {
    assert(
      !(await get(`/sessions/${fixture.id}/activity`)).some(
        (x) => x.task === "Section organization",
      ),
      "The one organizer attempt was already used",
    );
    const before = current;
    const selected = current.currentNote.sections[0];
    await page.getByLabel("Section to organize").selectOption(selected.id);
    const response = page.waitForResponse(
      (r) =>
        r.url().endsWith("/notes/organize") && r.request().method() === "POST",
      { timeout: 140000 },
    );
    await page
      .getByRole("button", { name: "Organize section", exact: true })
      .click();
    const result = await response,
      body = await result.json();
    snapshots.push({ httpStatus: result.status(), result: body });
    await save();
    assert.equal(result.status(), 200, body.error ?? "Organizer failed");
    current = await view();
    assert.equal(current.noteVersion, before.noteVersion + 1);
    assert(
      before.currentNote.sections
        .filter((x) => x.id !== selected.id)
        .every(
          (x) =>
            current.currentNote.sections.find((y) => y.id === x.id)
              ?.markdown === x.markdown,
        ),
      "Organizer changed another section",
    );
    await writeFile(
      path.join(folder, "db-organized-note.json"),
      JSON.stringify(current.currentNote, null, 2),
    );
  }
  if (["chat", "chat-read", "web", "web-read"].includes(phase)) {
    await page
      .getByRole("button", { name: "◇ Ask Playback", exact: true })
      .click();
    if (!readChat) {
      if (phase === "web") {
        assert(
          !(await get(`/sessions/${fixture.id}/activity`)).some(
            (x) => x.task === "Ask Playback web search",
          ),
          "The bounded web Q&A was already attempted",
        );
        await page.getByLabel(/Include public web search/).check();
      }
      await page
        .getByLabel("Your question", { exact: true })
        .fill(
          phase === "web"
            ? "What does Microsoft's official WinDbg documentation say about the .reload command and Microsoft Symbol Server? Keep public documentation separate from the lecture."
            : "What do the lecture sources say about kernel debugging? Explain only source-supported claims.",
        );
      const response = page.waitForResponse(
        (r) => r.url().endsWith("/ask/stream"),
        { timeout: 140000 },
      );
      await page.getByRole("button", { name: "Send", exact: true }).click();
      await page
        .getByRole("region", { name: "Ask Playback" })
        .locator("form")
        .dispatchEvent("submit");
      await page
        .getByRole("button", { name: "Working…", exact: true })
        .waitFor();
      await screenshot("loading");
      assert.equal((await response).status(), 200);
      await page.getByTestId("answer").waitFor({ timeout: 140000 });
      assert(
        (await page.getByTestId("answer").locator("details button").count()) >
          0,
        "Source-backed chat has no validated citations",
      );
      assert.equal(
        calls.filter((x) => x.endpoint.endsWith("/ask/stream")).length,
        1,
        "Duplicate send reached the API",
      );
      const conversations = await get(`/sessions/${fixture.id}/conversations`);
      assert(conversations.length > 0);
      snapshots.push({
        answer: await page.getByTestId("answer").innerText(),
        conversations,
      });
      if (phase === "web") {
        const records = await get(`/sessions/${fixture.id}/activity`);
        snapshots.push({
          webActivity: records.find(
            (x) => x.task === "Ask Playback web search",
          ),
        });
        await save();
        assert.equal(
          records.find((x) => x.task === "Ask Playback web search")?.status,
          "completed",
          "Live Google Search did not return verified sources",
        );
        assert(
          (await page
            .getByTestId("answer")
            .getByText("Public web:", { exact: true })
            .count()) > 0,
        );
      }
      await page.reload();
      await page
        .getByRole("heading", { name: fixture.title, exact: true })
        .waitFor();
      await page
        .getByRole("button", { name: "◇ Ask Playback", exact: true })
        .click();
    }
    await page.getByRole("button", { name: "Chat conversations" }).click();
    await page
      .getByRole("list", { name: "Saved conversations" })
      .getByRole("button")
      .first()
      .click();
    await page.getByTestId("answer").waitFor();
    assert.equal(
      calls.filter((x) => x.endpoint.endsWith("/ask/stream")).length,
      readChat ? 0 : 1,
      "Reading saved chat called a provider",
    );
    if (phase === "web-read") {
      assert(
        (await page
          .getByTestId("answer")
          .getByText("Public web:", { exact: true })
          .count()) > 0,
      );
      await page.getByTestId("answer").locator("details > summary").click();
      assert.equal(
        await page.getByTestId("answer").locator("details button").count(),
        7,
      );
      // Inspect the actual source-button target without visiting a third-party page.
      await page.evaluate(() => {
        window.__openedSource = null;
        window.open = (url) => {
          window.__openedSource = String(url);
          return null;
        };
      });
      await page
        .getByTestId("answer")
        .locator("details button")
        .first()
        .click();
      const openedSource = await page.evaluate(() => window.__openedSource);
      assert.match(openedSource, /^https:\/\//);
      snapshots.push({ openedSource });
      const record = (await get(`/sessions/${fixture.id}/activity`)).find(
        (x) => x.task === "Ask Playback web search",
      );
      assert.equal(record?.status, "completed");
      assert(record.usageJson);
      snapshots.push({ webActivity: record });
    }
    await screenshot("saved");
    await page.setViewportSize({ width: 375, height: 812 });
    await screenshot("narrow");
    const box = await page
      .getByRole("region", { name: "Ask Playback" })
      .boundingBox();
    assert(box.x >= 0 && box.x + box.width <= 376);
    assert(
      await page
        .getByTestId("answer")
        .evaluate((x) => x.scrollWidth <= x.clientWidth + 1),
      "Saved answer overflows the narrow chat",
    );
    if (readChat)
      assert.equal(calls.length, 0, "Reading saved chat generated a POST");
  } else {
    await page.getByRole("button", { name: "Preview", exact: true }).click();
    assert((await page.locator("[data-section-id]").count()) > 0);
    await page.getByRole("button", { name: "Sources", exact: true }).click();
    const source = page
      .getByTestId("note-content")
      .getByRole("button", { name: /^Open audio sources/ })
      .first();
    await source.click();
    const sources = page.getByRole("dialog", { name: "Grouped audio sources" });
    await sources.waitFor();
    assert(
      (await sources.getByRole("button", { name: /^Jump to/ }).count()) > 0,
    );
    await screenshot("sources");
    await sources.getByRole("button", { name: "Close sources" }).click();
    await page
      .getByRole("button", { name: "History & recovery", exact: true })
      .click();
    await page
      .getByRole("dialog", { name: "Note history & recovery" })
      .getByRole("button", { name: /^v1 ·/ })
      .click();
    await page
      .getByRole("dialog", { name: "Note history & recovery" })
      .locator("pre")
      .waitFor();
    await page.getByRole("button", { name: "Close note history" }).click();
    await page
      .locator(`summary[aria-label="Group options for ${fixture.groupName}"]`)
      .click();
    await page
      .getByRole("button", { name: "View AI flow", exact: true })
      .click();
    const flow = page.getByRole("dialog", {
      name: fixture.groupName + " · AI flow",
      exact: true,
    });
    await flow.waitFor();
    await flow.getByText("Recorded executions", { exact: true }).waitFor();
    await flow.getByLabel("Rendered flowchart").locator("svg").waitFor();
    const data = await get(
      `/groups/${fixture.groupId}/flow?sessionId=${fixture.id}`,
    );
    assert(
      data.executions.some(
        (x) =>
          x.task === "Note revision" &&
          x.status === "completed" &&
          x.promptVersion.startsWith("section-notes-v") &&
          x.promptText &&
          x.usageJson,
      ),
      "Group flow did not retain the actual prompt and usage",
    );
    snapshots.push({
      version: current.noteVersion,
      sourceStatuses: current.transcripts.reduce(
        (out, x) => ((out[x.noteStatus] = (out[x.noteStatus] ?? 0) + 1), out),
        {},
      ),
      executionCount: data.executions.length,
    });
    await screenshot("flow");
    await page.getByRole("button", { name: "Close AI flow" }).click();
    await page.getByRole("button", { name: "Reading", exact: true }).click();
    await screenshot("saved");
    await page.setViewportSize({ width: 760, height: 950 });
    await screenshot("narrow");
    await writeFile(
      path.join(folder, "db-session-after.json"),
      JSON.stringify(await view(), null, 2),
    );
    if (["read", "restart"].includes(phase))
      assert.equal(calls.length, 0, "Reading generated a POST");
  }
  assert.deepEqual(errors, []);
  passed = true;
} catch (error) {
  failure = error.message;
  await screenshot("failure").catch(() => {});
  throw error;
} finally {
  await save();
  await browser.close();
}
console.log(
  `Week 3 ${phase} passed; real API/Mongo evidence saved, no test data deleted.`,
);
