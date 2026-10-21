// Actual saved recording/API; no mocked endpoints and no model calls from this browser check.
import assert from "node:assert/strict";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { launchBrowser } from "./browser.mjs";
import { createServer } from "vite";
const web = fileURLToPath(new URL("..", import.meta.url));
const folder = "app-temp/data/validation/runs/2026-10-20-wave1-p1-recording";
const id = (await readFile(path.join(folder, "session-id.txt"), "utf8")).trim();
const artifacts = "output/playwright/wave1-live";
await mkdir(artifacts, { recursive: true });
const server = await createServer({ root: web, configFile: path.join(web, "vite.config.ts"), configLoader: "runner",
  server: { host: "127.0.0.1", port: 5188, strictPort: true, proxy: { "/api": "http://127.0.0.1:5088" } } });
await server.listen();
const browser = await launchBrowser();
const errors = [];
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  page.on("pageerror", e => errors.push(e.message));
  await page.goto("http://127.0.0.1:5188/");
  await page.getByRole("button", { name: /Wave 1 live recording/ }).first().click();
  await page.getByTestId("note-content").waitFor();
  assert.ok((await page.getByTestId("note-content").innerText()).length > 50);
  await page.getByRole("button", { name: "AI activity history", exact: true }).click();
  await page.getByRole("region", { name: "AI activity history" }).getByTestId("activity-entry").filter({ hasText: "Note revision" }).first().locator("summary").click();
  await page.getByRole("region", { name: "AI activity history" }).getByText(/qwen\/qwen3.8-flash/).first().waitFor();
  await page.screenshot({ path: path.join(artifacts, "desktop-activity.png"), fullPage: true });
  await page.keyboard.press("Escape");
  await page.getByRole("switch", { name: "Show sources" }).check();
  await page.getByTestId("note-content").getByRole("button", { name: /^Open audio sources/ }).first().click();
  await page.evaluate(() => window.addEventListener("playback-reveal-source", event => {
    window.wave1RevealedSource = event.detail.id;
  }, { once: true }));
  await page.getByRole("dialog", { name: "Grouped audio sources" }).getByRole("button", { name: /^Jump to/ }).first().click();
  await page.waitForFunction(() => {
    const row = document.getElementById(window.wave1RevealedSource ?? "");
    return row && row.getBoundingClientRect().height > 0;
  });
  await page.setViewportSize({ width: 375, height: 850 });
  await page.getByRole("button", { name: "Notes", exact: true }).click();
  await page.getByTestId("note-content").waitFor();
  await page.screenshot({ path: path.join(artifacts, "mobile-notes.png"), fullPage: true });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), "Mobile page overflowed");
  assert.deepEqual(errors, []);
  await writeFile(path.join(artifacts, "results.json"), JSON.stringify({ sessionId: id, passed: true, citationJump: true, errors, evidence: "Real saved Mongo/API recording; browser issued no generation request" }, null, 2));
  console.log("Wave 1 live browser passed: real saved notes, actual Qwen activity, desktop/mobile screenshots and layout");
} finally {
  await Promise.race([browser.close(), new Promise(resolve => setTimeout(resolve, 2000))]);
  await Promise.race([server.close(), new Promise(resolve => setTimeout(resolve, 2000))]);
}
process.exit(0);
