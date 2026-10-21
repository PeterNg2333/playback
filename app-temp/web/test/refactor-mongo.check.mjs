// Called only by the explicit local-Mongo acceptance runner; retains synthetic workspaces.
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {writeFile} from 'node:fs/promises';
import path from 'node:path';
import { launchBrowser } from "./browser.mjs";

export async function refactorMongoBrowser({origin, password, artifacts, restart}) {
  assert.equal(process.env.PLAYBACK_REFACTOR_MONGO, 'yes', 'Explicit Mongo opt-in is required');
  assert.equal(origin, 'http://127.0.0.1:5082');
  const browser = await launchBrowser();
  const suffix = randomUUID().slice(0, 8);
  const title = `Refactor retained browser ${suffix}`;
  const errors = [];
  try {
    const contexts = await Promise.all([browser.newContext(), browser.newContext()]);
    for (const context of contexts) await context.route('**/*', route => {
      const url = new URL(route.request().url());
      return url.origin === origin || ['data:', 'blob:'].includes(url.protocol) ? route.continue() : route.abort();
    });
    const pages = await Promise.all(contexts.map(context => context.newPage()));
    for (const [index, page] of pages.entries()) {
      page.on('pageerror', error => errors.push(error.message));
      await page.goto(origin + '/login');
      await page.getByLabel('Username', {exact: true}).fill(`refactor-${suffix}-${index}`);
      await page.getByLabel('Password', {exact: true}).fill(password);
      await page.getByRole('button', {name: 'Sign in', exact: true}).click();
      await page.getByRole('heading', {name: 'Transcript', exact: true}).waitFor();
    }
    const [owner, other] = contexts;
    const [page] = pages;
    async function api(context, method, resource, data, expected = 200) {
      const token = (await context.cookies()).find(cookie => cookie.name === 'Playback.Csrf')?.value;
      const response = await context.request.fetch(origin + '/api' + resource, {
        method, data, timeout: 15_000, headers: {Origin: origin, 'X-CSRF-TOKEN': token ?? ''}, maxRedirects: 0,
      });
      assert.equal(response.status(), expected, `${method} ${resource} status`);
      return response;
    }
    const health = await (await api(owner, 'GET', '/health')).json();
    assert.equal(health.database, 'playback_e2e'); assert.equal(health.mongo, true); assert.equal(health.automaticAsr, false);
    const anonymous = await browser.newContext();
    assert.equal((await anonymous.request.get(origin + '/api/sessions')).status(), 401);
    await anonymous.close();
    await page.getByRole('button', {name: 'New session', exact: true}).click();
    await page.getByRole('dialog').getByLabel('Session title', {exact: true}).fill(title);
    await page.getByRole('dialog').getByRole('button', {name: 'Save', exact: true}).click();
    await page.getByRole('button', {name: title, exact: true}).waitFor();
    const id = (await (await api(owner, 'GET', '/sessions')).json()).find(x => x.title === title)?.id;
    assert.match(id, /^[a-f0-9]{32}$/);
    await api(other, 'GET', `/sessions/${id}`, undefined, 404);
    assert.equal((await (await api(other, 'GET', '/sessions')).json()).some(x => x.id === id), false);
    const material = await (await api(owner, 'POST', `/sessions/${id}/materials`, {name: 'Fixture source', text: 'Capacity R/n applies only to equal shares at the bottleneck.'})).json();
    const markdown = `## TCP\n\nCapacity $R/n$ applies only at one bottleneck. [${material.id}]\n\n## Example\n\nFor n = 2 the equal share is $R/2$.`;
    await api(owner, 'POST', `/sessions/${id}/notes`, {markdown, basedOnVersion: 0});
    let state = await (await api(owner, 'GET', `/sessions/${id}`)).json();
    assert.equal(state.noteVersion, 1); assert.equal(state.currentNote.citations.length, 1);
    const originalBodies = state.currentNote.sections.flatMap(x => x.blocks).map(x => x.body);
    await api(owner, 'POST', `/sessions/${id}/notes/structure`, {basedOnVersion: 1, operations: [{kind: 'move-topic', topicId: state.currentNote.sections[1].id, parentId: state.currentNote.sections[0].id}]});
    state = await (await api(owner, 'GET', `/sessions/${id}`)).json();
    assert.equal(state.noteVersion, 2);
    assert.deepEqual(state.currentNote.sections.flatMap(x => x.blocks).map(x => x.body), originalBodies);
    assert.equal(state.currentNote.sections[1].parentId, state.currentNote.sections[0].id);
    await api(owner, 'POST', `/sessions/${id}/notes`, {markdown: 'stale edit', basedOnVersion: 1}, 409);
    await api(other, 'POST', `/sessions/${id}/notes`, {markdown: 'foreign edit', basedOnVersion: 2}, 404);
    const beforeRestart = state.noteMarkdown;
    await restart();
    state = await (await api(owner, 'GET', `/sessions/${id}`)).json();
    assert.equal(state.noteVersion, 2); assert.equal(state.noteMarkdown, beforeRestart);
    assert.equal(state.currentNote.citations[0].sourceIds.includes(material.id), true);
    await api(other, 'GET', `/sessions/${id}/notes/1`, undefined, 404);
    await api(owner, 'POST', `/sessions/${id}/notes/1/restore?basedOnVersion=2`);
    state = await (await api(owner, 'GET', `/sessions/${id}`)).json();
    assert.equal(state.noteVersion, 3); assert.equal(state.currentNote.citations.length, 1);
    assert.equal((await (await api(owner, 'GET', `/sessions/${id}/notes/history`)).json()).items.length, 3);
    await page.reload();
    await page.getByRole('button', {name: title, exact: true}).click();
    await page.getByText('Capacity', {exact: false}).first().waitFor();
    await page.setViewportSize({width: 390, height: 760});
    await page.screenshot({path: path.join(artifacts, 'mongo-browser-mobile.png'), fullPage: true});
    assert.deepEqual(errors, []);
    await writeFile(path.join(artifacts, 'mongo-browser.json'), JSON.stringify({sessionId: id, workspaceCount: 2, noteVersions: 3, restartedIsolatedHost: true, syntheticDataRetained: true, passed: true}, null, 2));
    console.log(`Refactor Mongo browser/API passed: two owners, cited hierarchy, settings UI/readback/conflicts, restart/history/restore; retained session ${id}.`);
  } finally {await browser.close();}
}
