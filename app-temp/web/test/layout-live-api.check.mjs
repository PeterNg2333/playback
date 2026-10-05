import { chromium } from "playwright-core";
import assert from "node:assert/strict";
import { mkdirSync } from "node:fs";

mkdirSync("app-temp/.artifacts", { recursive: true });

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
  await page.getByRole("heading", { name: "Lecture notes" }).waitFor();
  const health = await page.evaluate(() =>
    fetch("/api/health").then((response) => response.json()),
  );
  if (!health.mongo)
    await page
      .getByRole("alert")
      .filter({ hasText: "Local MongoDB is unavailable" })
      .waitFor();
  await page.getByRole("button", { name: "Start recording" }).waitFor();
  const columns = await page
    .locator('[data-testid="notes-panel"], [data-testid="transcript-panel"]')
    .evaluateAll((items) => items.map((x) => x.getBoundingClientRect().width));
  assert.ok(Math.abs(columns[0] - columns[1]) < 2, `columns: ${columns}`);
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page.getByLabel("Enable translation").waitFor();
  await page.getByRole("button", { name: "Transcript settings" }).click();
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("markdown");
  await page
    .getByLabel("Editable Markdown")
    .fill(
      "# Pipeline\n\n- Audio to notes\n\n```mermaid\nflowchart LR\nAudio --> Notes\n```",
    );
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("preview");
  await page.getByRole("switch", { name: "Show sources" }).uncheck();
  await page.getByLabel("Rendered flowchart").locator("svg").waitFor();
  const diagramText = await page
    .getByLabel("Rendered flowchart")
    .locator("svg")
    .textContent();
  assert.ok(
    diagramText?.includes("Audio") && diagramText.includes("Notes"),
    `diagram node: ${(
      await page
        .getByLabel("Rendered flowchart")
        .locator("svg .node")
        .first()
        .evaluate((x) => x.outerHTML)
    ).slice(0, 1200)}`,
  );
  await page.getByRole("button", { name: "Ask Playback" }).click();
  await page.getByRole("region", { name: "Ask Playback" }).waitFor();
  await page.screenshot({ path: "app-temp/.artifacts/browser-desktop.png" });
  await page.getByRole("button", { name: "Close chat" }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "Toggle sessions" }).click();
  assert.ok(
    await page
      .getByRole("navigation", { name: "Sessions and groups" })
      .isVisible(),
  );
  await page.getByRole("button", { name: "Close sessions" }).click();
  await page
    .getByRole("navigation", {
      name: "Sessions and groups",
      includeHidden: true,
    })
    .evaluate((element) =>
      element.getAnimations().map((animation) => animation.finish()),
    );
  await page
    .getByRole("navigation", { name: "Workspace views" })
    .getByRole("button", { name: "Notes" })
    .click();
  assert.equal(await page.locator("body").getAttribute("data-view"), "notes");
  await page.screenshot({ path: "app-temp/.artifacts/browser-mobile.png" });
  assert.deepEqual(errors, []);
  console.log(
    "Browser check passed: desktop columns, settings, Markdown diagram, chat, mobile navigation, no page errors",
  );
} finally {
  await browser.close();
}
