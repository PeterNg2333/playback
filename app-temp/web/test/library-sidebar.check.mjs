import assert from "node:assert/strict";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";

const browser = await chromium.launch({
  headless: true,
  executablePath:
    "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const groups = [{ id: "group-1", name: "Research" }];
const sessions = [{ id: "session-1", title: "Lecture 12", groupId: null }];
const errors = [];
let nextSession = 2;
let deleteFailure = false;
let deleteCalls = 0;
let capture = { state: "idle", sessionId: null, bytes: {}, error: null };

try {
  const page = await browser.newPage({
    viewport: { width: 1280, height: 800 },
  });
  page.on("pageerror", (error) => errors.push(error.message));
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace(/^\/api/, "");
    const method = request.method();
    const input = request.postDataJSON?.() || {};
    let data;
    let status = 200;
    if (path === "/health") data = { mongo: true, gemini: false, jev: false };
    else if (path === "/capture/status") data = capture;
    else if (path === "/groups" && method === "GET") data = groups;
    else if (path === "/groups" && method === "POST") {
      data = { id: `group-${groups.length + 1}`, name: input.name };
      groups.push(data);
    } else if (path.startsWith("/groups/") && method === "PUT") {
      data = groups.find((group) => group.id === path.split("/")[2]);
      data.name = input.name;
    } else if (path.startsWith("/groups/") && method === "DELETE") {
      const id = path.split("/")[2];
      groups.splice(
        groups.findIndex((group) => group.id === id),
        1,
      );
      for (const session of sessions)
        if (session.groupId === id) session.groupId = null;
      status = 204;
    } else if (path === "/sessions" && method === "GET") data = sessions;
    else if (path === "/sessions" && method === "POST") {
      data = {
        id: `session-${nextSession++}`,
        title: input.title,
        groupId: input.groupId,
      };
      sessions.unshift(data);
    } else if (path.endsWith("/group") && method === "PUT") {
      const session = sessions.find((item) => item.id === path.split("/")[2]);
      session.groupId = input.groupId;
      data = { id: session.id, groupId: session.groupId };
    } else if (path.startsWith("/sessions/") && method === "DELETE") {
      deleteCalls++;
      if (deleteFailure) {
        status = 409;
        data = { error: "Session is still processing" };
      } else {
        sessions.splice(
          sessions.findIndex((x) => x.id === path.split("/")[2]),
          1,
        );
        status = 204;
      }
    } else if (path.startsWith("/sessions/") && method === "GET") {
      const session = sessions.find((item) => item.id === path.split("/")[2]);
      data = {
        ...session,
        createdAt: "2026-09-27T00:00:00Z",
        noteMarkdown: "",
        noteVersion: 0,
        translationEnabled: false,
        translationLanguage: "zh-Hant",
        materials: [],
        transcripts: [],
        chunks: [],
        terms: [],
        currentNote: null,
      };
    } else throw new Error(`Unexpected API request: ${method} ${path}`);
    await route.fulfill({
      status,
      contentType: "application/json",
      body: status === 204 ? "" : JSON.stringify(data),
    });
  });

  await page.goto("http://127.0.0.1:5174/");
  await page.getByRole("heading", { name: "Sessions", exact: true }).waitFor();
  const group = page
    .getByTestId("session-group")
    .filter({ hasText: "Research" });
  const sessionsSection = page.getByRole("region", { name: "Sessions" });
  const newSession = page
    .locator("#sessions-heading")
    .locator("..")
    .getByRole("button", { name: "New session" });
  const newGroup = page
    .locator("#groups-heading")
    .locator("..")
    .getByRole("button", { name: "New group" });
  assert.equal(
    await newSession.count(),
    1,
    "New session belongs beside the Sessions heading",
  );
  for (const button of [newSession, newGroup])
    assert.notEqual(
      await button.evaluate(
        (element) => getComputedStyle(element).backgroundColor,
      ),
      "rgba(0, 0, 0, 0)",
    );
  await sessionsSection
    .getByRole("button", { name: "Lecture 12" })
    .dragTo(group.getByRole("button", { name: "Collapse Research" }));
  await group.getByRole("button", { name: "Lecture 12" }).waitFor();
  assert.equal(
    sessions.find((item) => item.id === "session-1").groupId,
    "group-1",
  );
  await group
    .getByRole("button", { name: "Lecture 12" })
    .dragTo(sessionsSection.getByRole("heading", { name: "Sessions" }));
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).waitFor();
  assert.equal(sessions.find((item) => item.id === "session-1").groupId, null);
  await sessionsSection
    .getByRole("button", { name: "Lecture 12" })
    .dragTo(group.getByRole("button", { name: "Collapse Research" }));
  await group.getByRole("button", { name: "Collapse Research" }).hover();
  assert.equal(
    await group
      .getByRole("button", { name: "New session in Research" })
      .evaluate((element) => getComputedStyle(element).opacity),
    "1",
  );
  await group.getByRole("button", { name: "New session in Research" }).click();
  await page
    .getByRole("dialog")
    .getByRole("textbox", { name: "Session title" })
    .fill("Research meeting");
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await group.getByRole("button", { name: "Research meeting" }).waitFor();
  assert.equal(
    sessions.find((item) => item.title === "Research meeting").groupId,
    "group-1",
  );
  const moveMenu = group.getByLabel("Session options for Lecture 12");
  await moveMenu.focus();
  await moveMenu.press("Enter");
  await group.getByRole("button", { name: "Move to Sessions" }).click();
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).waitFor();
  await sessionsSection.getByLabel("Session options for Lecture 12").click();
  await sessionsSection
    .getByRole("button", { name: "Move to Research" })
    .click();
  await group.getByRole("button", { name: "Lecture 12" }).waitFor();
  await page.screenshot({
    path: join(tmpdir(), "playback-sidebar-desktop.png"),
  });

  await group
    .locator('summary[aria-label="Group options for Research"]')
    .click();
  await group.getByRole("button", { name: "Rename group" }).click();
  await page
    .getByRole("dialog")
    .getByRole("textbox", { name: "Group name" })
    .fill("Research archive");
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await group
    .getByRole("button", { name: "Collapse Research archive" })
    .waitFor();
  await group
    .locator('summary[aria-label="Group options for Research archive"]')
    .click();
  await group.getByRole("button", { name: "Delete group" }).click();
  await page
    .getByRole("dialog", { name: "Delete Research archive?" })
    .getByRole("button", { name: "Delete group" })
    .click();
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).waitFor();
  await sessionsSection
    .getByRole("button", { name: "Research meeting" })
    .waitFor();
  assert.equal(groups.length, 0);
  assert.ok(sessions.every((item) => item.groupId === null));
  await page.setViewportSize({ width: 375, height: 812 });
  await page.getByRole("button", { name: "Toggle sessions" }).click();
  assert.ok(
    await page
      .getByRole("navigation", { name: "Sessions and groups" })
      .isVisible(),
  );
  assert.equal(
    await page.evaluate(
      () =>
        document
          .elementFromPoint(100, 130)
          ?.closest('nav[aria-label="Sessions and groups"]') !== null,
    ),
    true,
  );
  assert.equal(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
    true,
  );
  await page.screenshot({
    path: join(tmpdir(), "playback-sidebar-mobile.png"),
  });
  await page.setViewportSize({ width: 1280, height: 800 });
  const selectedId = sessions.find((x) => x.title === "Research meeting").id;
  assert.equal(
    await sessionsSection
      .getByRole("button", { name: "Research meeting", exact: true })
      .getAttribute("aria-current"),
    "page",
  );
  await sessionsSection.getByLabel("Session options for Lecture 12").click();
  await sessionsSection
    .getByRole("button", { name: "Delete session", exact: true })
    .click();
  const deletion = page.getByRole("dialog", {
    name: "Delete Lecture 12?",
    exact: true,
  });
  await deletion.getByRole("button", { name: "Cancel" }).click();
  assert.equal(deleteCalls, 0, "Cancel sent a delete request");
  await sessionsSection.getByLabel("Session options for Lecture 12").click();
  await sessionsSection
    .getByRole("button", { name: "Delete session", exact: true })
    .click();
  deleteFailure = true;
  await deletion
    .getByRole("button", { name: "Delete session", exact: true })
    .click();
  await deletion.getByRole("alert").waitFor();
  assert.equal(sessions.length, 2, "Failed deletion removed a session");
  deleteFailure = false;
  await deletion
    .getByRole("button", { name: "Delete session", exact: true })
    .click();
  await deletion.waitFor({ state: "hidden" });
  assert.equal(sessions.length, 1);
  assert.equal(
    sessions[0].id,
    selectedId,
    "Deleting another session changed the active session",
  );
  capture = { ...capture, state: "paused", sessionId: selectedId };
  await page.waitForTimeout(400);
  await sessionsSection
    .getByLabel("Session options for Research meeting")
    .click();
  await sessionsSection
    .getByRole("button", { name: "Delete session", exact: true })
    .click();
  const lastDeletion = page.getByRole("dialog", {
    name: "Delete Research meeting?",
    exact: true,
  });
  assert.equal(
    await lastDeletion
      .getByRole("button", { name: "Delete session", exact: true })
      .isDisabled(),
    true,
    "Paused recordings must be stopped before deleting",
  );
  await lastDeletion.getByRole("button", { name: "Cancel" }).click();
  capture = { ...capture, state: "idle" };
  await page
    .getByRole("combobox", { name: "Note view" })
    .selectOption("markdown");
  await page
    .getByLabel("Editable Markdown")
    .fill("Unsaved notes belonging to the deleted session");
  await sessionsSection
    .getByLabel("Session options for Research meeting")
    .click();
  await sessionsSection
    .getByRole("button", { name: "Delete session", exact: true })
    .click();
  await lastDeletion
    .getByRole("button", { name: "Delete session", exact: true })
    .click();
  await lastDeletion.waitFor({ state: "hidden" });
  assert.equal(sessions.length, 0);
  assert.equal(
    await page.getByLabel("Editable Markdown").inputValue(),
    "",
    "Deleted notes remained in the editor",
  );
  assert.equal(
    await page.getByRole("button", { name: "Save", exact: true }).isDisabled(),
    true,
  );
  assert.equal(
    await page.evaluate(() => localStorage.getItem("playback-session")),
    null,
  );
  assert.deepEqual(errors, []);
  console.log(
    "Sidebar check passed: drag in/out, keyboard move, hover create, group delete, mobile layout, session delete/cancel/failure, recording guard and final-session cleanup",
  );
} finally {
  await browser.close();
}
