// Real React dialog and local Edge, isolated fixture API; no providers or database.
import assert from 'node:assert/strict';
import {mkdir, writeFile} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createServer} from 'vite';
import {chromium} from 'playwright-core';
const web = fileURLToPath(new URL('..', import.meta.url));
const preview = path.join(web, 'output/rewrite'); await mkdir(preview, {recursive: true});
const body = 'Only at one bottleneck, $R/10$ applies. [source-a]';
const session = {id: 'rewrite-fixture', noteVersion: 4, noteMarkdown: body, materials: [], transcripts: [{id: 'source-a', sourceId: 'mic', original: 'One bottleneck', startMs: 0, endMs: 8000}], chunks: [], termInsights: [],
  currentNote: {schemaVersion: 2, sections: [{id: 'tcp', title: 'TCP', blocks: [], points: []}], citations: []}};
const proposal = {id: 'ready', sessionId: session.id, basedOnVersion: 4, topicId: 'tcp', createdAt: new Date().toISOString(), status: 'review-required',
  changes: [{targetBlockId: 'block-tcp', retainedBlockIds: ['block-tcp'], body: 'Reviewed context.\n\n' + body, role: 'definition'}], originalBodies: {'block-tcp': body}, issues: [], readyForAcceptance: true};
await writeFile(path.join(preview, 'index.html'), '<div id="root"></div><script type="module" src="./entry.tsx"></script>');
await writeFile(path.join(preview, 'entry.tsx'), `
import React, {useState} from 'react'; import {createRoot} from 'react-dom/client'; import {QueryClient, QueryClientProvider} from '@tanstack/react-query';
import {RewriteDialog} from '../../src/features/notes/RewriteDialog'; import '../../src/styles/app.css';
const client = new QueryClient({defaultOptions: {queries: {retry: false}}}); const session = ${JSON.stringify(session)};
function App() {const [version, setVersion] = useState(4); const [dirty, setDirty] = useState(false); return <>
<button onClick={() => setVersion(5)}>New version</button><button onClick={() => setDirty(true)}>Unsaved edit</button>
<RewriteDialog session={{...session, noteVersion: version}} draftDirty={dirty} onClose={() => {}} onOpenSource={() => {}} onPlay={() => {}} /></>;}
createRoot(document.getElementById('root')).render(<QueryClientProvider client={client}><App /></QueryClientProvider>);`);
const server = await createServer({root: web, configLoader: 'runner', server: {host: '127.0.0.1', port: 0}}); let browser;
try {
  await server.listen(); const origin = `http://127.0.0.1:${server.httpServer.address().port}`;
  browser = await chromium.launch({executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true, args: ['--disable-background-networking']});
  const context = await browser.newContext({serviceWorkers: 'block'}); const errors = []; let accepts = 0;
  await context.route('**/*', route => {const url = new URL(route.request().url()); if (url.origin !== origin) return route.abort();
    if (!url.pathname.startsWith('/api/')) return route.continue();
    if (route.request().method() === 'GET') return route.fulfill({json: [proposal, {...proposal, id: 'blocked', status: 'blocked-retention', issues: ['Rewrite lost a formula'], readyForAcceptance: false}]});
    accepts++; assert.equal(route.request().postDataJSON().semanticAccepted, true); return route.fulfill({status: 409, json: {error: 'Notes changed; reload'}});
  });
  const page = await context.newPage(); page.on('pageerror', e => errors.push(e.message)); await page.goto(origin + '/output/rewrite/index.html');
  const accept = page.getByRole('button', {name: 'Accept rewrite', exact: true});
  await page.getByText('Mechanical retention checks passed.', {exact: false}).waitFor(); assert.equal(await accept.isDisabled(), true);
  await page.locator('.math-formula math').first().waitFor();
  await page.getByLabel('I reviewed the meaning, qualifications, examples and source support.').check(); await accept.click();
  await page.getByRole('alert').waitFor(); assert.equal(accepts, 1); assert.match(await page.getByRole('alert').innerText(), /preview is retained/);
  await page.getByLabel('Saved preview').selectOption('blocked'); assert.equal(await accept.isDisabled(), true);
  await page.getByText('Retention checks blocked this preview.', {exact: false}).waitFor();
  await page.getByLabel('Saved preview').selectOption('ready');
  await page.getByRole('button', {name: 'New version', exact: true}).evaluate(el => el.click());
  await page.getByText('Notes changed since this preview.', {exact: false}).waitFor(); assert.equal(await accept.isDisabled(), true);
  await page.getByRole('button', {name: 'Unsaved edit', exact: true}).evaluate(el => el.click());
  await page.getByText('Save your editor changes before previewing or applying.').waitFor();
  await page.setViewportSize({width: 390, height: 844}); assert.ok((await page.getByRole('dialog').boundingBox()).width <= 390);
  assert.deepEqual(errors, []); console.log('Rewrite browser checks passed: original/proposed math, semantic opt-in, retention blocking, conflict preview retention, stale version, draft protection and mobile dialog (fixture API)');
} finally {await browser?.close(); await server.close();}
