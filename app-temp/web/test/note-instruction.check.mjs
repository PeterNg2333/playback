// Real React instruction dialog and block actions in local Edge with an isolated fixture API; no providers or database.
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";
import { chromium } from "playwright-core";
const web = fileURLToPath(new URL("..", import.meta.url));
const preview = path.join(web, "output/instruction");
await mkdir(preview, { recursive: true });
const block = {
  id: "block-a",
  order: 0,
  role: "detail",
  body: "Paging maps pages to frames.",
  userEdited: false,
  citationIds: [],
  anchors: [],
};
const session = {
  id: "instruction-fixture",
  noteVersion: 4,
  noteMarkdown: "",
  materials: [],
  transcripts: [],
  chunks: [],
  termInsights: [],
  currentNote: {
    schemaVersion: 2,
    sections: [
      {
        id: "paging",
        title: "Paging",
        blocks: [block],
        points: [],
        version: 1,
        markdown: "",
        userEdited: false,
      },
    ],
    citations: [],
  },
};
await writeFile(
  path.join(preview, "index.html"),
  '<div id="root"></div><script type="module" src="./entry.tsx"></script>',
);
await writeFile(
  path.join(preview, "entry.tsx"),
  `
import React, {useState} from 'react'; import {createRoot} from 'react-dom/client'; import {QueryClient, QueryClientProvider} from '@tanstack/react-query';
import {InstructionDialog} from '../../src/features/notes/InstructionDialog'; import {NoteBlockEditor} from '../../src/features/notes/NoteBlockEditor'; import '../../src/styles/app.css';
const client = new QueryClient({defaultOptions: {queries: {retry: false}}}); const session = ${JSON.stringify(session)};
function App() {const [open, setOpen] = useState(true); return <>
<NoteBlockEditor session={session} block={session.currentNote.sections[0].blocks[0]} />
{open && <InstructionDialog session={session} onClose={() => setOpen(false)} />}</>;}
createRoot(document.getElementById('root')).render(<QueryClientProvider client={client}><App /></QueryClientProvider>);`,
);
const server = await createServer({
  root: web,
  configLoader: "runner",
  server: { host: "127.0.0.1", port: 0 },
});
let browser;
try {
  await server.listen();
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`;
  browser = await chromium.launch({
    executablePath:
      "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    headless: true,
    args: ["--disable-background-networking"],
  });
  const context = await browser.newContext({ serviceWorkers: "block" });
  const errors = [];
  const posts = [];
  await context.route("**/*", (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== origin) return route.abort();
    if (!url.pathname.startsWith("/api/")) return route.continue();
    if (route.request().method() === "POST") {
      posts.push({ path: url.pathname, body: route.request().postDataJSON() });
      if (url.pathname.endsWith("/notes/instruct"))
        return route.fulfill({
          json: {
            note: {},
            applied: 1,
            notApplied: ["Protected block requires an explicit scope action"],
          },
        });
      return route.fulfill({ json: { version: 5 } });
    }
    return route.fulfill({
      json: {},
    });
  });
  const page = await context.newPage();
  page.on("pageerror", (e) => errors.push(e.message));
  await page.goto(origin + "/output/instruction/index.html");
  const apply = page.getByRole("button", {
    name: "Apply instruction",
    exact: true,
  });
  await apply.waitFor();
  assert.equal(await apply.isDisabled(), true);
  await page.getByLabel("Instruction target").selectOption("paging");
  await page
    .getByLabel("Instruction", { exact: true })
    .fill("Turn this into a table");
  await apply.click();
  await page
    .getByText("1 changes applied. Not applied: Protected block", {
      exact: false,
    })
    .waitFor();
  const instruct = posts.find((x) => x.path.endsWith("/notes/instruct"));
  assert.deepEqual(instruct.body, {
    basedOnVersion: 4,
    instruction: "Turn this into a table",
    topicId: "paging",
    blockId: null,
  });
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("button", { name: "Lock detail", exact: true }).click();
  await page.waitForFunction(() => true);
  await new Promise((resolve) => setTimeout(resolve, 300));
  const lock = posts.find((x) => x.path.endsWith("/notes/block/lock"));
  assert.deepEqual(lock.body, {
    basedOnVersion: 4,
    blockId: "block-a",
    locked: true,
  });
  await page
    .getByRole("button", { name: "Comment to AI", exact: true })
    .click();
  await page
    .getByRole("heading", { name: "Comment to AI on this text" })
    .waitFor();
  assert.equal(await page.getByLabel("Instruction target").count(), 0);
  await page.setViewportSize({ width: 390, height: 844 });
  assert.ok((await page.getByRole("dialog").boundingBox()).width <= 390);
  assert.deepEqual(errors, []);
  console.log(
    "Instruction browser checks passed: topic instruction request and partial-result message, block lock request, block comment dialog without target choice, mobile dialog (fixture API)",
  );
} finally {
  await browser?.close();
  await server.close();
}
