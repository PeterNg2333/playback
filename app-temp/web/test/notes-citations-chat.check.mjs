// Offline UI acceptance: fixture content verifies presentation, never generated-note quality.
import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import { chromium } from "playwright-core";

const id = "a".repeat(32);
const sourceId = `${id}-microphone-63926191107278790-${"b".repeat(64)}`;
const unknownId = `${id}-microphone-63926191107278790-${"c".repeat(64)}`;
const insightId = "d".repeat(64);
const nextSourceId = `${id}-microphone-63926191115278790-${"e".repeat(64)}`;
const secondId = "f".repeat(32);
const markdown = `# Resource allocation

## Main ideas
- **Bottleneck:** the resource limiting throughput. [${sourceId}]
- Start with the limiting resource. [${sourceId}, ${nextSourceId}] [${sourceId}] [[${sourceId}, ${unknownId}]]
- A bottlenecked resource is a word-boundary fixture.

### Process
\`\`\`mermaid
flowchart LR
  A["Identify bottleneck"] --> B["Allocate resources"]
\`\`\`
Supported process [${sourceId}]

## Key ideas
- Stored explanation [ref:${insightId}]
- Unknown reference [${unknownId}]
- Uncertainty [unclear]

Literal syntax: \`[${sourceId}]\`

\`\`\`text
[${unknownId}]
\`\`\`
[External link](https://example.com/reference)
`;
const session = {
  id,
  title: "Notes presentation fixture",
  createdAt: "2026-09-28T11:18:04Z",
  noteMarkdown: markdown,
  noteVersion: 1,
  noteLanguage: "en",
  groupId: "study-group",
  sourceGroups: [
    {
      id: "01",
      sourceId: "microphone",
      transcriptIds: [sourceId, nextSourceId],
      startMs: 0,
      endMs: 16000,
      recordedAt: "2026-09-28T11:18:04Z",
    },
  ],
  translationEnabled: false,
  translationLanguage: "en",
  materials: [],
  chunks: [],
  transcripts: [
    {
      id: sourceId,
      sourceId: "microphone",
      startMs: 0,
      endMs: 8000,
      original: "Identify the bottleneck and then allocate resources.",
      uncertain: false,
    },
    {
      id: nextSourceId,
      sourceId: "microphone",
      startMs: 8000,
      endMs: 16000,
      original: "Allocate resources.",
      uncertain: false,
    },
  ],
  terms: [{ text: "Bottleneck", transcriptIds: [sourceId], materialIds: [] }],
  termInsights: [
    {
      id: insightId,
      term: "Bottleneck",
      highlight: true,
      outputLanguage: "en",
      transcriptIds: [sourceId],
      materialIds: [],
      explanation: "The resource that limits overall throughput.",
      evidence: [
        {
          title: "Saved explanation source",
          url: "https://example.com/bottleneck",
        },
      ],
    },
  ],
};
await mkdir("output/playwright", { recursive: true });
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
  timezoneId: "UTC",
});
const page = await context.newPage();
const errors = [];
page.on("pageerror", (error) => errors.push(error.message));
const audioRequests = [];
// A short local PCM fixture exercises the real browser's queue without recording or uploading audio.
const wav = Buffer.alloc(44 + 3200);
wav.write("RIFF", 0);
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
let mode = "offline",
  asks = 0;
let holdReply = false;
let releaseReply;
let activities = [];
const conversations = new Map();
let conversationNumber = 0;
await context.route(/^https:\/\//, (route) => route.abort());
await page.route("**/api/**", async (route) => {
  const path = new URL(route.request().url()).pathname;
  if (path.startsWith("/api/chunks/") && path.endsWith("/audio")) {
    audioRequests.push(path.split("/")[3]);
    return route.fulfill({ contentType: "audio/wav", body: wav });
  }
  if (path.endsWith("/ask/stream")) {
    asks++;
    if (mode === "offline") return route.abort("connectionrefused");
    if (mode === "proxy-error") return route.fulfill({ status: 500, body: "" });
    if (mode === "provider-error")
      return route.fulfill({
        status: 503,
        json: { error: "Provider temporarily unavailable" },
      });
    const input = route.request().postDataJSON();
    const answer = {
      questionId: "reply-" + asks,
      answer: `- A bottleneck limits throughput. [${sourceId}]`,
      evidence: [{ kind: "lecture", id: sourceId, label: "11:18:04" }],
      inference: false,
    };
    if (mode === "table") {
      answer.answer =
        "| 機制 | 用途 | 認證 | 加密 | 限制 | 補充 |\n| --- | --- | --- | --- | --- | --- |\n" +
        Array.from(
          { length: 14 },
          (_, n) =>
            `| 比較 ${n + 1} | 保護連線 | 憑證 | 對稱加密 | 一般說明 | 請核對來源 |`,
        ).join("\n");
      answer.lectureStatus = "unverified";
      answer.evidence = [];
      answer.inference = true;
      answer.webError =
        "Google Search returned no verifiable web sources. " +
        "Check source availability. ".repeat(20);
    }
    const conversation = conversations.get(input.conversationId);
    assert(
      conversation && conversation.sessionId === id,
      "Answers must stay in the selected lecture conversation",
    );
    conversation.title = input.question.slice(0, 60);
    conversation.turns.push({
      id: input.requestId,
      question: input.question,
      answer,
      createdAt: new Date().toISOString(),
    });
    if (holdReply)
      await new Promise((resolve) => {
        releaseReply = resolve;
      });
    return route.fulfill({
      contentType: "application/x-ndjson",
      body: JSON.stringify({ type: "result", answer }) + "\n",
    });
  }
  let json;
  if (path === "/api/health")
    json = {
      mongo: true,
      gemini: true,
      jev: true,
      groundedChatFallback: true,
      sessionLanguageSettings: true,
      recordingSourceSelection: true,
      chatConversations: true,
      aiActivity: true,
    };
  else if (path === "/api/capture/status") json = { state: "idle", bytes: {} };
  else if (path === "/api/sessions")
    json = [
      { id, title: session.title, groupId: session.groupId },
      { id: secondId, title: "Other lecture" },
    ];
  else if (path === "/api/groups")
    json = [{ id: session.groupId, name: "Study group" }];
  else if (path === `/api/sessions/${id}`) json = session;
  else if (path === `/api/sessions/${secondId}`)
    json = {
      ...session,
      id: secondId,
      title: "Other lecture",
      groupId: null,
      noteMarkdown: "Other isolated notes",
      sourceGroups: [],
      transcripts: [],
      termInsights: [],
      terms: [],
    };
  else if (path.endsWith("/activity")) json = activities;
  else if (path.endsWith("/conversations")) {
    const sessionId = path.split("/")[3];
    if (route.request().method() === "POST") {
      json = {
        id: "conversation-" + ++conversationNumber,
        sessionId,
        title: "New conversation",
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        turns: [],
      };
      conversations.set(json.id, json);
    } else
      json = [...conversations.values()].filter(
        (item) => item.sessionId === sessionId,
      );
  } else if (path.includes("/conversations/"))
    json = conversations.get(path.split("/").at(-1));
  else
    return route.fulfill({
      status: 404,
      json: { error: "No fixture for this request" },
    });
  return route.fulfill({ json });
});
try {
  const testUrl = process.env.PLAYBACK_WEB_TEST_URL ?? "http://127.0.0.1:5174";
  assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(testUrl));
  await page.goto(testUrl);
  const content = page.getByTestId("note-content");
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("preview");
  await page.getByRole("switch", { name: "Show sources" }).check();
  await content
    .getByRole("button", {
      name: "Open audio sources 00:00–00:08",
      exact: true,
    })
    .first()
    .waitFor();
  assert.equal(
    await content
      .getByRole("listitem")
      .filter({ hasText: "Start with the limiting resource" })
      .getByRole("button", { name: "Open audio sources 00:00–00:16" })
      .count(),
    1,
    "Repeated citations in a point must collapse into one grouped source",
  );
  await content
    .getByRole("listitem")
    .filter({ hasText: "Start with the limiting resource" })
    .getByRole("button", { name: "Open audio sources 00:00–00:16" })
    .click();
  const sourcePanel = page.getByRole("dialog", {
    name: "Grouped audio sources",
  });
  assert.equal(
    await sourcePanel.getByRole("button", { name: /^Jump to/ }).count(),
    2,
  );
  const nextAudio = page.waitForResponse((response) =>
    response.url().endsWith(`/chunks/${nextSourceId}/audio`),
  );
  await sourcePanel
    .getByRole("button", { name: "Play combined passage", exact: true })
    .click();
  await nextAudio;
  assert.deepEqual(
    [...new Set(audioRequests)],
    [sourceId, nextSourceId],
    "Combined passage must play every chunk in sequence",
  );
  assert(
    await sourcePanel.isVisible(),
    "Player updates must not close the source popup",
  );
  await sourcePanel.getByRole("button", { name: "Close sources" }).click();
  assert.equal(
    await content.getByText("Source unavailable", { exact: true }).count(),
    2,
  );
  assert.equal(
    await content.locator("code").filter({ hasText: sourceId }).count(),
    1,
  );
  assert.equal(
    await content.locator("code").filter({ hasText: unknownId }).count(),
    1,
  );
  assert.equal(
    await content.locator("a[href='https://example.com/reference']").count(),
    1,
  );
  await content.getByText("Uncertainty [unclear]", { exact: true }).waitFor();
  await content.getByLabel("Rendered flowchart").locator("svg").waitFor();
  assert.match(
    await content.getByLabel("Rendered flowchart").locator("svg").textContent(),
    /Identify bottleneck/,
  );
  const topics = page.getByRole("navigation", { name: "Note topics" });
  assert.equal(await topics.getByRole("button").count(), 4);
  await topics.getByRole("button", { name: "Key ideas", exact: true }).click();
  assert.equal(
    await content
      .getByRole("heading", { name: "Key ideas", exact: true })
      .evaluate((el) => el === document.activeElement),
    true,
  );
  assert.equal(
    await content
      .getByRole("button", { name: "Explain bottlenecked", exact: true })
      .count(),
    0,
  );
  await content
    .getByRole("button", { name: "Explain Bottleneck", exact: true })
    .hover();
  await page
    .getByRole("dialog", { name: "Bottleneck explanation" })
    .getByText(session.termInsights[0].explanation)
    .waitFor();
  await page.waitForTimeout(2200);
  assert(
    await page
      .getByRole("dialog", { name: "Bottleneck explanation" })
      .isVisible(),
    "Background refresh must preserve an open term popup",
  );
  await page.screenshot({ path: "output/playwright/notes-term-hover.png" });
  await page.getByRole("button", { name: "Ask a follow-up in chat" }).click();
  assert.match(
    await page.getByLabel("Your question", { exact: true }).inputValue(),
    /Bottleneck/,
  );
  await page.getByRole("button", { name: "Close chat" }).click();
  await content
    .getByRole("button", { name: "Open audio sources 00:00–00:16" })
    .first()
    .click();
  await page
    .getByRole("dialog", { name: "Grouped audio sources" })
    .getByRole("button", { name: "Jump to 11:18:04" })
    .click();
  await page.locator(`[id='${sourceId}']`).waitFor();
  activities = [
    {
      id: "running-note",
      sessionId: id,
      task: "Note revision",
      status: "running",
      provider: "fixture",
      model: "fixture",
      startedAt: new Date().toISOString(),
      basedOnVersion: 1,
      sourceIds: [sourceId],
    },
  ];
  await page
    .getByRole("status")
    .getByText("Analyzing…", { exact: true })
    .waitFor();
  activities[0].draft = `## Draft topic\n\n- A short point [01].`;
  await page
    .getByRole("status")
    .getByText("Editing…", { exact: true })
    .waitFor();
  assert.equal(
    await page.getByRole("region", { name: "Live note draft" }).count(),
    0,
    "Preview must not append the live draft",
  );
  await page.getByRole("combobox", { name: "Note view" }).selectOption("draft");
  await page
    .getByRole("region", { name: "Live note draft" })
    .getByRole("heading", { name: "Draft topic" })
    .waitFor();
  await page.screenshot({ path: "output/playwright/notes-live-draft.png" });
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("markdown");
  await page.getByLabel("Editable Markdown").fill("My unsaved edit");
  activities[0].draft += "\n- Another point.";
  await page.waitForTimeout(900);
  assert.equal(
    await page.getByLabel("Editable Markdown").inputValue(),
    "My unsaved edit",
  );
  await page.getByLabel("Editable Markdown").fill(markdown);
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("preview");
  await page.getByRole("switch", { name: "Show sources" }).uncheck();
  activities = [];
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page.getByLabel("Enable translation").waitFor();
  assert.match(
    await page.getByTestId("settings-menu").textContent(),
    /Traditional Chinese/,
  );
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page
    .getByRole("button", { name: "◇ Ask Playback", exact: true })
    .click();
  const input = page.getByRole("textbox", { name: "Your question" });
  await input.fill("What is the bottleneck?");
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("alert")
    .getByText(/Cannot reach the Playback backend/)
    .waitFor();
  assert.equal(await input.inputValue(), "What is the bottleneck?");
  assert.equal(asks, 1, "A failed question must not be retried automatically");
  for (const failure of ["proxy-error", "provider-error"]) {
    mode = failure;
    await page.getByRole("button", { name: "Send", exact: true }).click();
    await page
      .getByRole("region", { name: "Ask Playback" })
      .getByRole("alert")
      .getByText(
        failure === "proxy-error"
          ? /backend is unavailable/
          : /Provider temporarily unavailable/,
      )
      .waitFor();
    assert.equal(await input.inputValue(), "What is the bottleneck?");
  }
  mode = "success";
  holdReply = true;
  await input.press("Shift+Enter");
  assert.match(await input.inputValue(), /\n/);
  await input.press("Enter");
  await page.waitForFunction(
    () => document.querySelector("#chat-input").value === "",
  );
  await page
    .getByRole("status")
    .getByText("Thinking…", { exact: true })
    .waitFor();
  await input.fill("My next question");
  assert.equal(asks, 4, "Sending while busy must not duplicate the request");
  await input.press("Enter");
  assert.equal(asks, 4);
  holdReply = false;
  releaseReply();
  await page.getByTestId("answer").getByRole("listitem").waitFor();
  assert.equal(
    await input.inputValue(),
    "My next question",
    "A completed send must preserve the next draft",
  );
  assert.equal(
    await page
      .getByRole("region", { name: "Ask Playback" })
      .getByRole("alert")
      .count(),
    0,
  );
  assert.equal(asks, 4);
  const firstConversationId = [...conversations.keys()][0];
  await page.getByRole("button", { name: "Chat conversations" }).click();
  assert.equal(
    await page
      .locator("#chat-session optgroup[label='Study group'] option")
      .count(),
    1,
  );
  await page.getByRole("button", { name: "＋ New chat", exact: true }).click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("article")
    .first()
    .waitFor({ state: "detached" });
  assert.equal(
    await page
      .getByRole("region", { name: "Ask Playback" })
      .getByRole("article")
      .count(),
    0,
  );
  assert.equal(
    conversations.size,
    1,
    "New chat must not save an empty history item before a question is sent",
  );
  await input.fill("How should resources be allocated?");
  await page.getByRole("button", { name: "Send", exact: true }).click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("article")
    .getByText("How should resources be allocated?", { exact: true })
    .waitFor();
  await page.getByRole("button", { name: "Chat conversations" }).click();
  await page
    .getByRole("list", { name: "Saved conversations" })
    .getByRole("button", {
      name: conversations.get(firstConversationId).title,
      exact: true,
    })
    .click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("article")
    .getByText("What is the bottleneck?", { exact: true })
    .waitFor();
  for (let n = 1; n <= 24; n++) {
    const updatedAt = new Date(Date.now() - n * 60_000).toISOString();
    conversations.set(`history-${n}`, {
      id: `history-${n}`,
      sessionId: id,
      title: `TLS mechanism ${n} comparison with certificate verification and key exchange`,
      createdAt: updatedAt,
      updatedAt,
      turns: [],
    });
  }
  await page.reload();
  await page
    .getByRole("button", { name: "◇ Ask Playback", exact: true })
    .click();
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("article")
    .getByText("What is the bottleneck?", { exact: true })
    .waitFor();
  await page.getByRole("button", { name: "Chat conversations" }).click();
  const history = page.getByRole("dialog", {
    name: "Chat history",
    exact: true,
  });
  const search = history.getByRole("searchbox", {
    name: "Search recent chats",
  });
  assert(
    await search.evaluate((el) => document.activeElement === el),
    "Opening history must focus search",
  );
  assert.equal(
    await history
      .getByRole("list", { name: "Saved conversations" })
      .getByRole("button")
      .count(),
    26,
  );
  await search.fill("TLS mechanism 7 ");
  assert.equal(
    await history
      .getByRole("list", { name: "Saved conversations" })
      .getByRole("button")
      .count(),
    1,
  );
  await search.fill("No such conversation");
  await history.getByText("No matching chats.", { exact: true }).waitFor();
  await search.fill("");
  await search.press("ArrowDown");
  assert(
    await history
      .locator("[data-chat-row]")
      .first()
      .evaluate((el) => document.activeElement === el),
  );
  await page.keyboard.press("ArrowUp");
  assert(await search.evaluate((el) => document.activeElement === el));
  await page
    .getByLabel("Lecture session", { exact: true })
    .selectOption(secondId);
  await page
    .getByRole("heading", { name: "Other lecture", exact: true })
    .waitFor();
  await page
    .getByText("No conversations in this session yet.", { exact: true })
    .waitFor();
  assert.equal(
    await page
      .getByRole("region", { name: "Ask Playback" })
      .getByRole("article")
      .count(),
    0,
    "A different lecture must never show another lecture's conversation",
  );
  await page.getByLabel("Lecture session", { exact: true }).selectOption(id);
  await page
    .getByRole("region", { name: "Ask Playback" })
    .getByRole("article")
    .getByText("What is the bottleneck?", { exact: true })
    .waitFor();
  await page.keyboard.press("Escape");
  mode = "table";
  await input.fill("用中文 table 比較機制");
  await input.press("Enter");
  const messages = page.getByTestId("chat-messages");
  await page
    .getByText("General answer · Not verified against sources", { exact: true })
    .waitFor();
  assert.equal(await input.inputValue(), "");
  const table = page.getByRole("table", { name: "Table" });
  assert.equal(await table.getByRole("row").count(), 15);
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="chat-messages"]');
    return el.scrollHeight - el.scrollTop - el.clientHeight < 2;
  });
  await messages.evaluate((el) => {
    el.scrollTop = 0;
  });
  await page
    .getByRole("button", { name: "↓ Latest reply", exact: true })
    .waitFor();
  await page
    .getByText("Web sources unavailable", { exact: true })
    .evaluate((el) => el.click());
  await page.waitForTimeout(150);
  assert.equal(
    await messages.evaluate((el) => el.scrollTop),
    0,
    "Growing answers must not pull the reader away from earlier messages",
  );
  await page
    .getByRole("button", { name: "↓ Latest reply", exact: true })
    .click();
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="chat-messages"]');
    return el.scrollHeight - el.scrollTop - el.clientHeight < 2;
  });
  await page
    .getByText("Web sources unavailable", { exact: true })
    .evaluate((el) => el.click());
  for (const [name, width, height, zoom] of [
    ["desktop", 1440, 1000, 1],
    ["desktop-125", 1440, 1000, 1.25],
    ["tablet", 1024, 768, 1],
    ["mobile", 390, 844, 1],
    ["narrow", 320, 720, 1],
  ]) {
    await page.setViewportSize({ width, height });
    await page.locator("html").evaluate((el, value) => {
      el.style.zoom = String(value);
    }, zoom);
    const chat = await page
      .getByRole("region", { name: "Ask Playback" })
      .boundingBox();
    const field = await input.boundingBox();
    assert(
      chat &&
        chat.x >= 0 &&
        chat.y >= 0 &&
        chat.x + chat.width <= width + 1 &&
        chat.y + chat.height <= height + 1,
      `Chat bounds at ${name}: ${JSON.stringify(chat)}`,
    );
    assert(
      field &&
        field.y >= chat.y &&
        field.y + field.height <= chat.y + chat.height + 1,
      `Input bounds at ${name}`,
    );
    if (name === "desktop")
      assert(
        chat.width <= 480 && chat.height <= 600,
        "Quick chat must stay compact",
      );
    assert(
      await messages.evaluate((el) => el.scrollWidth <= el.clientWidth + 1),
      `Chat horizontal overflow at ${name}`,
    );
    if (name === "narrow") {
      assert(
        await table.evaluate((el) => el.scrollWidth > el.clientWidth),
        "Wide tables must scroll within the reply",
      );
      await table.evaluate((el) => {
        el.scrollLeft = 80;
      });
      assert(
        await table.evaluate((el) => el.scrollLeft > 0),
        "Table horizontal scrolling must work",
      );
    }
    assert(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
      `Horizontal overflow at ${name}`,
    );
    await page.screenshot({ path: `output/playwright/notes-chat-${name}.png` });
    await page.getByRole("button", { name: "Chat conversations" }).click();
    const historyBounds = await history.boundingBox();
    assert(
      historyBounds &&
        historyBounds.x >= chat.x &&
        historyBounds.x + historyBounds.width <= chat.x + chat.width + 1 &&
        historyBounds.y + historyBounds.height <= chat.y + chat.height,
      `History bounds at ${name}`,
    );
    assert(
      await history
        .getByRole("list", { name: "Saved conversations" })
        .evaluate(
          (el) => el.parentElement.scrollHeight > el.parentElement.clientHeight,
        ),
      "Long history must scroll within the picker",
    );
    assert(
      await history
        .locator("[data-chat-row] span")
        .last()
        .evaluate((el) => el.scrollWidth > el.clientWidth),
      "Long chat titles must truncate to one line",
    );
    await page.screenshot({
      path: `output/playwright/notes-chat-history-${name}.png`,
    });
    await page.keyboard.press("Escape");
    await history.waitFor({ state: "detached" });
    assert(
      await page
        .getByRole("button", { name: "Chat conversations" })
        .evaluate((el) => document.activeElement === el),
      "Escape must return focus to history control",
    );
  }
  await page.locator("html").evaluate((el) => {
    el.style.zoom = "1";
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole("button", { name: "Close chat" }).click();
  await page.getByTestId("notes-body").evaluate((el) => {
    el.scrollTop = 0;
  });
  await page.screenshot({ path: "output/playwright/notes-topic-tree.png" });
  assert.deepEqual(errors, []);
  console.log(
    "Offline notes/chat check passed: source chips, diagrams, saved terms, failures/retry, clear-on-send and next draft, unverified warning, tables, automatic scrolling and reader position, searchable history and keyboard focus, deferred conversation creation, conversation isolation, desktop/zoom/mobile bounds.",
  );
} finally {
  await browser.close();
}
