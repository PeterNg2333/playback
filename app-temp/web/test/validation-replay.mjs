// Offline browser acceptance using saved real responses. No provider requests are allowed.
import assert from "node:assert/strict";
import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { chromium } from "playwright-core";
const out = path.resolve("app-temp/data/validation/runs/2026-09-28-repair");
const read = async (name) =>
  JSON.parse(
    (await readFile(path.join(out, name), "utf8")).replace(/^\uFEFF/, ""),
  );
let session = await read("hardware-session.json");
const originalMarkdown = session.noteMarkdown;
const answer = (await read("chat-mismatch-reused.json")).response;
const savedActivity = await read("hardware-activity.json");
let activities = Array.isArray(savedActivity)
  ? savedActivity
  : savedActivity.value;
const health = {
  ...(await read("app-health.json")),
  aiActivity: true,
  groundedChatFallback: true,
};
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
  hasTouch: true,
});
const page = await context.newPage();
const errors = [];
page.on("pageerror", (e) => errors.push(e.message));
let asks = 0,
  mode = "success";
await page.route("**/api/**", async (route) => {
  const req = route.request(),
    url = new URL(req.url()),
    p = url.pathname;
  let value;
  if (p === "/api/health") value = health;
  else if (p === "/api/sessions")
    value = [
      { id: session.id, title: session.title },
      { id: "f".repeat(32), title: "Other isolated fixture" },
    ];
  else if (p === "/api/groups") value = [];
  else if (p === "/api/capture/status") value = { state: "idle", bytes: {} };
  else if (p.endsWith("/activity")) value = activities;
  else if (p.endsWith("/notes/edits")) value = [];
  else if (p.endsWith("/ask/stream")) {
    asks++;
    await new Promise((r) => setTimeout(r, 600));
    const events =
      mode === "success"
        ? [
            { type: "draft", text: "Checking public evidence…" },
            { type: "result", answer },
          ]
        : mode === "error"
          ? [
              {
                type: "error",
                error:
                  "Provider timeout. Your question is retained; retry explicitly.",
              },
            ]
          : [{ type: "draft", text: "Incomplete fixture" }];
    return route.fulfill({
      contentType: "application/x-ndjson",
      body: events.map((e) => JSON.stringify(e) + "\n").join(""),
    });
  } else if (p === `/api/sessions/${session.id}`) value = session;
  else if (p === `/api/sessions/${"f".repeat(32)}`)
    value = {
      ...session,
      id: "f".repeat(32),
      title: "Other isolated fixture",
      noteMarkdown: "Other session",
      transcripts: [],
      materials: [],
      chunks: [],
    };
  else
    return route.fulfill({
      status: 404,
      json: { error: "Unprovided offline fixture " + p },
    });
  return route.fulfill({ json: value });
});
// Block every other external request, including Search suggestion iframe assets.
await context.route(/^https:\/\//, (route) => route.abort());
await page.addInitScript((id) => {
  if (window === window.top) localStorage.setItem("playback-session", id);
}, session.id);
const shot = (name) =>
  page.screenshot({ path: path.join(out, "replay-" + name + ".png") });
const visible = async (locator) => {
  assert(await locator.isVisible());
  const b = await locator.boundingBox();
  assert(
    b &&
      b.x >= 0 &&
      b.y >= 0 &&
      b.x + b.width <= page.viewportSize().width + 1 &&
      b.y + b.height <= page.viewportSize().height + 1,
  );
};
let passed = false;
try {
  await page.goto("http://127.0.0.1:5181");
  await page
    .getByRole("heading", { name: session.title, exact: true })
    .waitFor();
  const history = page.getByRole("button", {
    name: "AI activity history",
    exact: true,
  });
  await history.hover();
  await page.locator("#notes-activity").waitFor();
  await visible(page.locator("#notes-activity"));
  await page.locator("#notes-activity").hover();
  await shot("history-desktop");
  await page.keyboard.press("Escape");
  assert.equal(await page.locator("#notes-activity").count(), 0);
  await page.getByRole("button", { name: "Edit", exact: true }).focus();
  await history.focus();
  await page.locator("#notes-activity").waitFor();
  await page.keyboard.press("Escape");
  await page.mouse.move(0, 0);
  await history.click();
  await page.mouse.move(0, 0);
  assert(await page.locator("#notes-activity").isVisible());
  await page.keyboard.press("Escape");
  session = {
    ...session,
    noteVersion: session.noteVersion + 1,
    noteMarkdown:
      "```mermaid\nflowchart LR\nAudio --> Notes\n```\n\nStable diagram fixture.",
  };
  const svg = page
    .getByTestId("notes-body")
    .getByLabel("Rendered flowchart")
    .locator("svg");
  await svg.waitFor({ timeout: 9000 });
  const diagramId = await svg.getAttribute("id");
  await page.waitForTimeout(2200);
  assert.equal(
    await svg.getAttribute("id"),
    diagramId,
    "Activity polling must not remount an unchanged diagram",
  );
  // Browser scroll anchor survives insertion above the reader; tail followers follow appends.
  session = {
    ...session,
    noteVersion: session.noteVersion + 1,
    noteMarkdown: Array.from(
      { length: 65 },
      (_, i) => `Stable paragraph ${i}. Source content stays here.`,
    ).join("\n\n"),
  };
  await page
    .getByText("Stable paragraph 30. Source content stays here.", {
      exact: true,
    })
    .waitFor({ timeout: 8000 });
  const body = page.getByTestId("notes-body");
  await body.evaluate((el) => {
    el.scrollTop = 900;
    el.dispatchEvent(new Event("scroll", { bubbles: true }));
  });
  const anchor = await body.evaluate((el) => {
    const top = el.getBoundingClientRect().top;
    const p = [...el.querySelectorAll("[data-markdown] > *")].find(
      (p) => p.getBoundingClientRect().bottom > top,
    );
    return { text: p.textContent, offset: p.getBoundingClientRect().top - top };
  });
  session = {
    ...session,
    noteVersion: session.noteVersion + 1,
    noteMarkdown: "New introduction.\n\n" + session.noteMarkdown,
  };
  await page
    .getByText("New introduction.", { exact: true })
    .waitFor({ timeout: 8000 });
  const delta = await body.evaluate((el, a) => {
    const p = [...el.querySelectorAll("[data-markdown] > *")].find(
      (p) => p.textContent === a.text,
    );
    return (
      p.getBoundingClientRect().top - el.getBoundingClientRect().top - a.offset
    );
  }, anchor);
  assert(Math.abs(delta) < 3);
  await body.evaluate((el) => {
    el.scrollTop = el.scrollHeight;
    el.dispatchEvent(new Event("scroll", { bubbles: true }));
  });
  session = {
    ...session,
    noteVersion: session.noteVersion + 1,
    noteMarkdown: session.noteMarkdown + "\n\nNew tail paragraph.",
  };
  await page
    .getByText("New tail paragraph.", { exact: true })
    .waitFor({ timeout: 8000 });
  assert(
    await body.evaluate(
      (el) => el.scrollHeight - el.scrollTop - el.clientHeight < 3,
    ),
  );
  await shot("scroll-tail");
  // Dirty editor and selection must survive an actual polling refresh and streamed note draft.
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("markdown");
  const editor = page.getByLabel("Editable Markdown");
  await editor.fill("My unsaved explanation\nSecond line");
  await editor.evaluate((el) => {
    el.focus();
    el.setSelectionRange(3, 10);
  });
  session = {
    ...session,
    noteVersion: session.noteVersion + 1,
    noteMarkdown: originalMarkdown + "\n\nSaved revision fixture",
  };
  activities = [
    {
      id: "offline-note",
      sessionId: session.id,
      task: "Note revision",
      status: "running",
      provider: "fixture replay",
      model: "recorded Gemini output",
      startedAt: new Date().toISOString(),
      sourceIds: [],
      basedOnVersion: session.noteVersion,
      draft: "An incremental draft…",
    },
  ];
  await page.waitForTimeout(4500);
  assert.equal(
    await editor.inputValue(),
    "My unsaved explanation\nSecond line",
  );
  assert.deepEqual(
    await editor.evaluate((el) => [el.selectionStart, el.selectionEnd]),
    [3, 10],
  );
  await shot("dirty-caret-stream");
  activities = [];
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("preview");
  await page.getByRole("switch", { name: "Show sources" }).uncheck();
  await page
    .getByRole("button", { name: "◇ Ask Playback", exact: true })
    .click();
  await page.getByLabel("Your question", { exact: true }).fill("What is cache");
  await page
    .getByRole("checkbox", { name: "Search the web", exact: true })
    .check();
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .locator("form")
    .dispatchEvent("submit");
  await page.getByRole("button", { name: "Working…", exact: true }).waitFor();
  await shot("chat-loading");
  await page.getByTestId("answer").waitFor();
  assert.equal(asks, 1);
  assert.match(await page.getByTestId("answer").innerText(), /hypothesis/);
  for (const width of [1440, 1024, 768, 375, 320]) {
    await page.setViewportSize({ width, height: 900 });
    await page.waitForTimeout(350);
    await visible(page.getByRole("region", { name: "Ask Playback" }));
    await visible(page.getByRole("button", { name: "Send", exact: true }));
    assert(
      await page
        .getByRole("button", { name: "Send", exact: true })
        .evaluate((el) => {
          const r = el.getBoundingClientRect();
          return el.contains(
            document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2),
          );
        }),
    );
    assert(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    );
    await shot("chat-" + width);
  }
  mode = "error";
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("alert")
    .waitFor();
  assert.equal(
    await page.getByLabel("Your question", { exact: true }).inputValue(),
    "What is cache",
  );
  await shot("chat-error");
  mode = "incomplete";
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("alert")
    .getByText(/stream ended before completion/)
    .waitFor();
  await shot("chat-incomplete");
  await page.getByRole("button", { name: "Close chat", exact: true }).click();
  await page
    .getByRole("navigation", { name: "Workspace views" })
    .getByRole("button", { name: "Notes", exact: true })
    .click();
  await history.tap();
  await visible(page.locator("#notes-activity"));
  await shot("history-touch-320");
  await page.keyboard.press("Escape");
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page
    .getByRole("button", { name: "◇ Ask Playback", exact: true })
    .click();
  mode = "success";
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await page
    .getByRole("button", { name: "Other isolated fixture", exact: true })
    .click();
  await page
    .getByRole("heading", { name: "Other isolated fixture", exact: true })
    .waitFor();
  await page.waitForTimeout(1200);
  assert.equal(
    await page.getByTestId("answer").count(),
    0,
    "Old session answer must not appear in new session",
  );
  assert.deepEqual(errors, []);
  passed = true;
} finally {
  await writeFile(
    path.join(out, "offline-replay.json"),
    JSON.stringify(
      {
        testedAt: new Date().toISOString(),
        passed,
        kind: "offline replay of saved provider results; no new provider calls",
        asks,
        errors,
      },
      null,
      2,
    ),
  );
  await browser.close();
}
console.log(
  "Offline saved-response acceptance passed: responsive chat, duplicate send, timeout/incomplete stream, session isolation, activity hover/focus/touch, dirty caret, reader anchor and tail following.",
);
