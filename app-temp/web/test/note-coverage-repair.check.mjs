// Real saved Week 3 snapshot + deterministic repair replay, no provider requests.
import assert from "node:assert/strict";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { chromium } from "playwright-core";
const run = "../data/validation/runs/2026-09-29-note-coverage";
const folder = "output/playwright/note-coverage";
await mkdir(folder, { recursive: true });
const before = JSON.parse(await readFile(`${run}/session-before.json`, "utf8"));
const result = JSON.parse(
  await readFile(`${run}/post-guard/offline-results.json`, "utf8"),
);
const firstNote = JSON.parse(
  await readFile(`${run}/post-guard/offline-note-v147.json`, "utf8"),
);
let session = structuredClone(before),
  report = structuredClone(result.initial),
  fail = false,
  unknownReference = false,
  deferAll = false,
  writes = [],
  errors = [],
  repairBody;
const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
try {
  const context = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
  });
  await context.route(/^https:\/\//, (route) => route.abort());
  const page = await context.newPage();
  page.setDefaultTimeout(12000);
  page.on("pageerror", (e) => errors.push(e.message));
  await page.route("**/api/**", async (route) => {
    const request = route.request(),
      url = new URL(request.url()),
      path = url.pathname;
    if (request.method() !== "GET") writes.push(path);
    let json;
    if (path === "/api/health")
      json = {
        mongo: true,
        gemini: false,
        jev: false,
        sectionNotes: true,
        noteCoverage: true,
        sessionSync: true,
        aiActivity: true,
        recordingSourceSelection: true,
      };
    else if (path === "/api/capture/status")
      json = { state: "idle", bytes: {} };
    else if (path === "/api/sessions")
      json = [
        { id: before.id, title: "Week 3 coverage replay", groupId: null },
      ];
    else if (path === "/api/groups" || /\/(activity|conversations)$/.test(path))
      json = [];
    else if (path.endsWith("/sync")) {
      const { transcripts, chunks, materials, terms, termInsights, ...meta } =
        session;
      json = {
        cursor: "fixture",
        reset: true,
        hasMore: false,
        changes: [
          { kind: "meta", value: { ...meta, groupId: null } },
          ...transcripts.map((value) => ({ kind: "transcript", value })),
          ...chunks.map((value) => ({ kind: "chunk", value })),
        ],
      };
    } else if (path.endsWith("/notes/coverage")) json = report;
    else if (path.endsWith("/notes/repair")) {
      repairBody = request.postDataJSON();
      if (unknownReference)
        return route.fulfill({
          status: 400,
          json: { error: "AI note contains an unknown term reference" },
        });
      if (deferAll)
        return route.fulfill({ json: { version: session.noteVersion } });
      if (fail)
        return route.fulfill({
          status: 409,
          json: {
            error: "Synthetic stale-base rejection; existing notes retained",
          },
        });
      session = {
        ...session,
        noteVersion: firstNote.version,
        noteMarkdown: firstNote.markdown,
        currentNote: firstNote,
      };
      const remaining = report.gaps[0].sourceIds.slice(32),
        first = before.transcripts.find((x) => x.id === remaining[0]);
      report = {
        ...report,
        version: firstNote.version,
        referenced: 78,
        unreferenced: 1078,
        completedWithoutReference: 1078,
        gaps: [
          {
            ...report.gaps[0],
            startMs: first.startMs,
            sourceIds: remaining,
            completedWithoutReference: 1078,
          },
        ],
      };
      json = { version: firstNote.version };
    } else if (path === `/api/sessions/${before.id}`) json = session;
    else
      return route.fulfill({
        status: 404,
        json: { error: `Unhandled offline request ${path}` },
      });
    return route.fulfill({ json });
  });
  await page.goto(process.env.COVERAGE_BASE_URL || "http://127.0.0.1:5174");
  await page
    .getByRole("button", { name: "Week 3 coverage replay", exact: true })
    .click();
  const coverage = page.getByRole("complementary", {
    name: "Transcript reference coverage",
  });
  const reviewButton = coverage.getByRole("button", {
    name: "Review references",
  });
  const review = page.getByRole("dialog", { name: "Review note references" });
  await coverage.getByText("1,110 transcript parts without links").waitFor();
  const toolbar = page.getByTestId("notes-toolbar");
  assert.equal(
    await toolbar.getByRole("button", { name: "Save", exact: true }).count(),
    1,
  );
  assert.equal(
    await toolbar.getByRole("button", { name: "Review references" }).count(),
    1,
  );
  assert.equal(
    await page.getByTestId("notes-panel").locator(":scope > footer").count(),
    0,
  );
  await page.screenshot({ path: folder + "/coverage-closed.png" });
  const notesBefore = await page.getByTestId("notes-body").boundingBox();
  assert.equal(
    await page
      .getByRole("button", { name: "Revise with AI", exact: true })
      .isVisible(),
    false,
  );
  await reviewButton.click();
  assert.match(
    await review.innerText(),
    /1,110 parts were previously marked processed/,
  );
  assert.equal(
    Math.round((await page.getByTestId("notes-body").boundingBox()).height),
    Math.round(notesBefore.height),
    "Review must not shrink the note reading area",
  );
  assert.equal(writes.length, 0, "Opening review must not start AI repair");
  await page.screenshot({ path: folder + "/reference-review-desktop.png" });
  await review
    .getByRole("button", { name: /open transcript/ })
    .first()
    .click();
  await review.waitFor({ state: "hidden" });
  await page.locator('[id="' + before.transcripts[0].id + '"]').waitFor();
  await page.screenshot({ path: folder + "/coverage-gap-revealed.png" });
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("markdown");
  const editor = page.getByLabel("Editable Markdown");
  await editor.fill(before.noteMarkdown + "\n\nUser unsaved correction.");
  await reviewButton.click();
  assert.equal(
    await review
      .getByRole("button", { name: "Repair next gap with AI" })
      .isDisabled(),
    true,
  );
  await review.getByText("Save your edits before repairing.").waitFor();
  await review.getByRole("button", { name: "Close reference review" }).click();
  await editor.fill(before.noteMarkdown);
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("preview");
  await reviewButton.click();
  await review.getByRole("button", { name: "Repair next gap with AI" }).click();
  await review
    .getByText(/1,078 parts were previously marked processed/)
    .waitFor();
  assert.deepEqual(repairBody, { basedOnVersion: 146 });
  assert.equal(
    session.currentNote.sections[0].markdown,
    before.currentNote.sections[0].markdown,
  );
  fail = true;
  await review.getByRole("button", { name: "Repair next gap with AI" }).click();
  await review
    .getByRole("alert")
    .getByText(/Synthetic stale-base/)
    .waitFor();
  assert.equal(session.noteVersion, 147);
  fail = false;
  unknownReference = true;
  await review.getByRole("button", { name: "Repair next gap with AI" }).click();
  await review
    .getByRole("alert")
    .getByText("The AI returned an invalid explanation link.")
    .waitFor();
  await review.getByText("Error details", { exact: true }).click();
  await review
    .getByText("AI note contains an unknown term reference", { exact: true })
    .waitFor();
  unknownReference = false;
  deferAll = true;
  await review.getByRole("button", { name: "Repair next gap with AI" }).click();
  await review
    .getByRole("status")
    .getByText(/No note changes were saved/)
    .waitFor();
  assert.equal(
    session.noteVersion,
    147,
    "An all-deferred response must not imply a saved revision",
  );
  await review.getByRole("button", { name: "Close reference review" }).click();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "Notes", exact: true }).click();
  assert.equal(await reviewButton.isVisible(), true);
  for (const width of [320, 390]) {
    await page.setViewportSize({ width, height: 844 });
    const header = await toolbar.boundingBox();
    for (const control of [
      toolbar.getByRole("button", { name: "Save", exact: true }),
      reviewButton,
      toolbar.getByRole("combobox", { name: "Note view" }),
    ]) {
      const box = await control.boundingBox();
      assert(
        box.x >= header.x &&
          box.x + box.width <= header.x + header.width + 1 &&
          Math.abs(box.y + box.height / 2 - (header.y + header.height / 2)) < 4,
        "Header controls must fit on one line",
      );
    }
  }
  await page.screenshot({ path: folder + "/coverage-narrow.png" });
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
    true,
  );
  await reviewButton.click();
  await page.screenshot({ path: folder + "/reference-review-mobile.png" });
  const bounds = await review.boundingBox();
  assert(
    bounds.x >= 0 &&
      bounds.width <= 390 &&
      bounds.y >= 0 &&
      bounds.y + bounds.height <= 844,
    "Reference dialog must fit mobile viewport",
  );
  assert.equal(
    await review
      .getByRole("button", { name: "Repair next gap with AI" })
      .isVisible(),
    true,
  );
  await page.keyboard.press("Escape");
  await review.waitFor({ state: "hidden" });
  assert.equal(
    await reviewButton.evaluate((e) => e === document.activeElement),
    true,
  );
  assert.deepEqual(errors, []);
  await writeFile(
    `${folder}/results.json`,
    JSON.stringify(
      {
        evidence:
          "Offline browser replay, saved Week 3 snapshot; no provider or DB writes",
        errors,
        writes,
        oldCompletedDetected: true,
        earliestSourceRevealed: true,
        dirtyEditProtected: true,
        existingSectionRetained: true,
        staleRepairVisible: true,
        narrowNoOverflow: true,
      },
      null,
      2,
    ),
  );
  console.log(
    "Coverage UI passed: old completed gaps, earliest-source reveal, dirty edit protection, versioned repair, stale error and narrow viewport. Offline snapshot replay.",
  );
} finally {
  await browser.close();
}
