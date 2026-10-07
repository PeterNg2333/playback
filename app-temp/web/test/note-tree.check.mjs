// Actual React note preview/edit/outline components in a local browser; fixture API only.
import assert from "node:assert/strict";
import {mkdir, writeFile} from "node:fs/promises";
import path from "node:path";
import {fileURLToPath} from "node:url";
import {createServer} from "vite";
import {chromium} from "playwright-core";

const web = fileURLToPath(new URL("..", import.meta.url));
const preview = path.join(web, "output/tree");
const artifacts = path.resolve(web, "../../output/playwright/note-tree");
await mkdir(preview, {recursive: true}); await mkdir(artifacts, {recursive: true});
const block = (id, body, source) => ({id, body, order: 0, role: "definition", userEdited: false, citationIds: [source], anchors: [{id, offset: 0, length: body.length, sourceIds: [source]}]});
const section = (id, title, parentId, body, source) => ({id, title, schemaVersion: 2, parentId, order: 0, version: 1, userEdited: false,
  markdown: body ? `## ${title}\n\n${body}` : "", blocks: body ? [block("block-" + id, body, source)] : [], points: body ? [{id: "block-" + id, text: body, sourceIds: [source]}] : []});
const sections = [section("tcp", "TCP", null, "Shared capacity $R/n$, only at one bottleneck. [source-a]", "source-a"),
  section("sharing", "Sharing", "tcp", "", ""), section("example", "Worked example", "sharing", "Example: n=10 gives R/10. [source-b]", "source-b")];
const session = {id: "tree-fixture", title: "TCP", createdAt: "2026-10-08T01:00:00Z", noteVersion: 4,
  noteMarkdown: "saved tree", materials: [], transcripts: [{id: "source-a", sourceId: "microphone", original: "Shared bottleneck", startMs: 0, endMs: 8000},
    {id: "source-b", sourceId: "microphone", original: "Ten flows", startMs: 8000, endMs: 16000}], chunks: [], termInsights: [],
  currentNote: {schemaVersion: 2, presentation: "numbered-hierarchy", sections, citations: []}};
await writeFile(path.join(preview, "index.html"), '<div id="root"></div><script type="module" src="./entry.tsx"></script>');
await writeFile(path.join(preview, "entry.tsx"), `
import React, {useState} from 'react'; import {createRoot} from 'react-dom/client';
import {NotePreview} from '../../src/features/notes/NotePreview'; import {OrganizeSection} from '../../src/features/notes/OrganizeSection';
import '../../src/styles/app.css'; const session = ${JSON.stringify(session)};
function App() {const [version, setVersion] = useState(4); const [outline, setOutline] = useState(false); return <div className="p-5">
<button onClick={() => setVersion(5)}>Receive newer version</button><button onClick={() => setOutline(true)}>Outline</button>
<NotePreview session={{...session, noteVersion: version}} markdown={session.noteMarkdown} reading={false} onReference={() => {}} onOpenSource={() => {}} onPlay={() => {}} />
{outline && <OrganizeSection session={{...session, noteVersion: version}} draftDirty={false} onClose={() => setOutline(false)} />}</div>;} createRoot(document.getElementById('root')).render(<App />);
`);
const server = await createServer({root: web, configLoader: "runner", server: {host: "127.0.0.1", port: 0}});
let browser;
try {
  await server.listen(); const address = server.httpServer.address(); const origin = `http://127.0.0.1:${address.port}`;
  browser = await chromium.launch({executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe", headless: true});
  const context = await browser.newContext(); let saved;
  await context.route("**/*", async route => {
    const url = new URL(route.request().url());
    if (url.origin !== origin) return route.abort();
    if (url.pathname.startsWith("/api/")) {saved = route.request().postDataJSON(); return route.fulfill({status: 409, contentType: "application/json", body: JSON.stringify({error: "Notes changed; reload"})});}
    return route.continue();
  });
  const page = await context.newPage(); await page.goto(origin + "/output/tree/index.html");
  await page.getByRole("heading", {name: "1 TCP", exact: true}).waitFor();
  assert.equal(await page.getByRole("heading", {name: "1.1 Sharing", exact: true}).count(), 1);
  assert.equal(await page.getByRole("heading", {name: "1.1.1 Worked example", exact: true}).count(), 1);
  await page.locator(".math-formula math").first().waitFor();
  await page.getByRole("button", {name: "Edit definition"}).first().click();
  await page.getByRole("textbox", {name: "Note text"}).fill("A retained user draft [source-a]");
  await page.getByRole("button", {name: "Receive newer version"}).evaluate(el => el.click());
  await page.getByRole("button", {name: "Save text"}).click();
  await page.getByRole("alert").waitFor();
  assert.equal(saved.basedOnVersion, 4); assert.equal(saved.blockId, "block-tcp");
  assert.equal(await page.getByRole("textbox", {name: "Note text"}).inputValue(), "A retained user draft [source-a]");
  await page.getByRole("button", {name: "Cancel", exact: true}).click();
  await page.getByRole("button", {name: "Outline", exact: true}).click();
  await page.locator("#organize-section").selectOption("example");
  await page.getByLabel("Move topic in the outline").check();
  await page.getByLabel("Parent topic", {exact: true}).selectOption("tcp");
  await page.getByRole("button", {name: "Move topic", exact: true}).click();
  await page.getByRole("alert").waitFor(); assert.equal(saved.operations[0].parentId, "tcp");
  await page.getByRole("button", {name: "Cancel", exact: true}).click();
  await page.setViewportSize({width: 390, height: 844});
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await page.screenshot({path: path.join(artifacts, "tree-mobile.png"), fullPage: true});
  console.log("Note tree browser checks passed: numbered hierarchy, math and source preview, protected block edit draft/conflict, outline proposal and mobile width (fixture API)");
} finally {await browser?.close(); await server.close();}
