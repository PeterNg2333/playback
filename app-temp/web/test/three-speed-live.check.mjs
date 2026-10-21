import assert from "node:assert/strict";
import path from "node:path";
import { launchBrowser } from "./browser.mjs";

// The three-speed live run's saved session in the real page (checks/Validation/three-speed-live.mjs --browser starts an
// offline API on playback_e2e and Vite, then calls this): the finished topics and their tree, source chips that resolve
// to transcript rows, a chip that jumps to its passage, the summary, and phone width. Requests leave only for the local
// API; anything on https is aborted.
export async function checkSession({ web, folder, session, results }) {
  const browser = await launchBrowser();
  const errors = [];
  try {
    const context = await browser.newContext({
      viewport: { width: 1440, height: 1000 },
    });
    await context.route(/^https:\/\//, (route) => route.abort());
    await context.addInitScript(
      (id) => localStorage.setItem("playback-session", id),
      session.id,
    );
    const page = await context.newPage();
    page.setDefaultTimeout(30_000);
    page.on("pageerror", (error) => errors.push(error.message));
    await page.goto(web);
    await page
      .getByRole("heading", { name: session.title, exact: true })
      .waitFor();

    const content = page.getByTestId("note-content");
    await page
      .getByRole("combobox", { name: "Note view" })
      .selectOption("preview");
    await page.getByRole("switch", { name: "Show sources" }).check();
    const topics = page
      .getByRole("navigation", { name: "Note topics" })
      .getByRole("button");
    await topics.first().waitFor();
    const shown = await topics.allInnerTexts();
    const expected = results.final.titles.flatMap((t) => [
      t.title,
      ...t.children,
    ]);
    // The tree numbers headings ("1) Title", "1.2) Title").
    for (const title of expected)
      assert(
        shown.some(
          (label) => label.trim().replace(/^[\d.]+\)\s+/, "") === title,
        ),
        `Topic tree is missing "${title}": ${JSON.stringify(shown)}`,
      );

    const chips = content.getByRole("button", {
      name: /^(Open audio sources|Jump to source at)/,
    });
    await chips.first().waitFor();
    const chipCount = await chips.count();
    const unavailable = await content
      .getByText("Source unavailable", { exact: true })
      .count();
    assert(
      chipCount >= results.final.topics,
      `Only ${chipCount} source chips for ${results.final.topics} topics`,
    );
    assert.equal(
      unavailable,
      0,
      "A note cites a source the session does not have",
    );

    // A chip in the middle of the notes jumps to its transcript row.
    const chip = chips.nth(Math.floor(chipCount / 2));
    const label = await chip.getAttribute("aria-label");
    await chip.scrollIntoViewIfNeeded();
    await chip.click();
    const popup = page.getByRole("dialog", { name: "Grouped audio sources" });
    if (await popup.isVisible().catch(() => false))
      await popup
        .getByRole("button", { name: /^Jump to/ })
        .first()
        .click();
    await page.locator("[data-revealed]").waitFor();
    const revealed = await page.locator("[data-revealed]").getAttribute("id");
    assert(
      revealed?.startsWith(session.id),
      `The chip revealed ${revealed}, not a row of this session`,
    );
    await page.screenshot({ path: path.join(folder, "browser-desktop.png") });

    await page
      .getByRole("combobox", { name: "Note view" })
      .selectOption("preview");
    const text = await content.innerText();
    assert(text.length > 5_000, "The notes body is unexpectedly short");

    await page.setViewportSize({ width: 390, height: 844 });
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - window.innerWidth,
    );
    assert(
      overflow <= 1,
      `The page scrolls sideways by ${overflow}px at phone width`,
    );
    await page.screenshot({ path: path.join(folder, "browser-mobile.png") });
    assert.deepEqual(errors, [], "The page threw");
    return {
      topicsInTree: shown.length,
      sourceChips: chipCount,
      unavailable,
      jumpedFrom: label,
      revealed,
      notesCharacters: text.length,
    };
  } finally {
    await browser.close();
  }
}
