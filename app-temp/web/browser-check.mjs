import { chromium } from "playwright-core";
import assert from "node:assert/strict";

const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
try {
  const page = await browser.newPage({
    viewport: { width: 1440, height: 900 },
  });
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.goto("http://127.0.0.1:5173/");
  await page.getByRole("heading", { name: "Notes" }).waitFor();
  const health = await page.evaluate(() => fetch("/api/health").then((response) => response.json()));
  if (!health.mongo)
    await page.getByRole("alert").filter({ hasText: "Local MongoDB is unavailable" }).waitFor();
  await page.getByRole("button", { name: "Start Recording" }).waitFor();
  const columns = await page
    .locator(".panel")
    .evaluateAll((items) => items.map((x) => x.getBoundingClientRect().width));
  assert.ok(Math.abs(columns[0] - columns[1]) < 2, `columns: ${columns}`);
  await page.getByRole("button", { name: "Theme" }).click();
  await page.getByRole("button", { name: "Wave" }).click();
  assert.equal(await page.locator("body").getAttribute("data-theme"), "wave");
  await page.getByRole("button", { name: "Markdown" }).click();
  await page
    .getByLabel("Editable Markdown")
    .fill(
      "# Pipeline\n\n- Audio to notes\n\n```mermaid\nflowchart LR\nAudio --> Notes\n```",
    );
  await page.getByRole("button", { name: "Preview" }).click();
  await page.locator(".flowchart svg").waitFor();
  const diagramText = await page.locator(".flowchart svg").textContent();
  assert.ok(
    diagramText?.includes("Audio") && diagramText.includes("Notes"),
    `diagram node: ${(
      await page
        .locator(".flowchart svg .node")
        .first()
        .evaluate((x) => x.outerHTML)
    ).slice(0, 1200)}`,
  );
  await page.getByRole("button", { name: "Ask Playback" }).click();
  await page
    .locator(".chat-head strong", { hasText: "Ask Playback" })
    .waitFor();
  await page.screenshot({ path: "../browser-desktop.png" });
  await page.setViewportSize({ width: 390, height: 844 });
  await page
    .getByRole("navigation", { name: "Workspace views" })
    .getByRole("button", { name: "Notes" })
    .click();
  assert.equal(await page.locator("body").getAttribute("data-view"), "notes");
  await page.screenshot({ path: "../browser-mobile.png" });
  assert.deepEqual(errors, []);
  console.log(
    "Browser check passed: desktop columns, theme, Markdown diagram, chat, mobile tabs, no page errors",
  );
} finally {
  await browser.close();
}
