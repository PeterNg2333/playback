import assert from "node:assert/strict";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { chromium } from "playwright-core";

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
});
const groups = [{ id: "group-1", name: "Research" }];
const sessions = [{ id: "session-1", title: "Lecture 12", groupId: null }];
const errors = [];
let nextSession = 2;

try {
  const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
  page.on("pageerror", (error) => errors.push(error.message));
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname.replace(/^\/api/, "");
    const method = request.method();
    const input = request.postDataJSON?.() || {};
    let data;
    let status = 200;
    if (path === "/health") data = { mongo: true, gemini: false, jev: false };
    else if (path === "/capture/status") data = { state: "idle", sessionId: null, bytes: {}, error: null };
    else if (path === "/groups" && method === "GET") data = groups;
    else if (path === "/groups" && method === "POST") {
      data = { id: `group-${groups.length + 1}`, name: input.name };
      groups.push(data);
    } else if (path.startsWith("/groups/") && method === "PUT") {
      data = groups.find((group) => group.id === path.split("/")[2]);
      data.name = input.name;
    } else if (path.startsWith("/groups/") && method === "DELETE") {
      const id = path.split("/")[2];
      groups.splice(groups.findIndex((group) => group.id === id), 1);
      for (const session of sessions) if (session.groupId === id) session.groupId = null;
      status = 204;
    } else if (path === "/sessions" && method === "GET") data = sessions;
    else if (path === "/sessions" && method === "POST") {
      data = { id: `session-${nextSession++}`, title: input.title, groupId: input.groupId };
      sessions.unshift(data);
    } else if (path.endsWith("/group") && method === "PUT") {
      const session = sessions.find((item) => item.id === path.split("/")[2]);
      session.groupId = input.groupId;
      data = { id: session.id, groupId: session.groupId };
    } else if (path.startsWith("/sessions/") && method === "GET") {
      const session = sessions.find((item) => item.id === path.split("/")[2]);
      data = {
        ...session, createdAt: "2026-09-27T00:00:00Z", noteMarkdown: "",
        noteVersion: 0, translationEnabled: false, translationLanguage: "zh-Hant",
        externalProcessingConsent: false, materials: [], transcripts: [], chunks: [],
        terms: [], currentNote: null,
      };
    } else throw new Error(`Unexpected API request: ${method} ${path}`);
    await route.fulfill({ status, contentType: "application/json", body: status === 204 ? "" : JSON.stringify(data) });
  });

  await page.goto("http://127.0.0.1:5174/");
  await page.getByRole("heading", { name: "Sessions", exact: true }).waitFor();
  const group = page.locator(".session-group").filter({ hasText: "Research" });
  const sessionsSection = page.locator(".sessions-section");
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).dragTo(group.locator(".folder-row"));
  await group.getByRole("button", { name: "Lecture 12" }).waitFor();
  assert.equal(sessions.find((item) => item.id === "session-1").groupId, "group-1");
  await group.getByRole("button", { name: "Lecture 12" }).dragTo(sessionsSection.locator(".sidebar-heading"));
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).waitFor();
  assert.equal(sessions.find((item) => item.id === "session-1").groupId, null);
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).dragTo(group.locator(".folder-row"));
  await group.locator(".folder-row").hover();
  assert.equal(await group.locator(".folder-create").evaluate((element) => getComputedStyle(element).opacity), "1");
  await group.getByRole("button", { name: "New session in Research" }).click();
  await page.getByRole("dialog").getByRole("textbox", { name: "Session title" }).fill("Research meeting");
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await group.getByRole("button", { name: "Research meeting" }).waitFor();
  assert.equal(sessions.find((item) => item.title === "Research meeting").groupId, "group-1");
  const moveMenu = group.locator(".session-entry").filter({ hasText: "Lecture 12" }).locator("summary");
  await moveMenu.focus();
  await moveMenu.press("Enter");
  await group.getByRole("button", { name: "Move to Sessions" }).click();
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).waitFor();
  await sessionsSection.locator(".session-entry").filter({ hasText: "Lecture 12" }).locator("summary").click();
  await sessionsSection.getByRole("button", { name: "Move to Research" }).click();
  await group.getByRole("button", { name: "Lecture 12" }).waitFor();
  await page.screenshot({ path: join(tmpdir(), "playback-sidebar-desktop.png") });

  await group.locator('summary[aria-label="Group options for Research"]').click();
  await group.getByRole("button", { name: "Rename group" }).click();
  await page.getByRole("dialog").getByRole("textbox", { name: "Group name" }).fill("Research archive");
  await page.getByRole("dialog").getByRole("button", { name: "Save" }).click();
  await group.getByRole("button", { name: "Collapse Research archive" }).waitFor();
  await group.locator('summary[aria-label="Group options for Research archive"]').click();
  await group.getByRole("button", { name: "Delete group" }).click();
  await page.getByRole("dialog", { name: "Delete Research archive?" }).getByRole("button", { name: "Delete group" }).click();
  await sessionsSection.getByRole("button", { name: "Lecture 12" }).waitFor();
  await sessionsSection.getByRole("button", { name: "Research meeting" }).waitFor();
  assert.equal(groups.length, 0);
  assert.ok(sessions.every((item) => item.groupId === null));
  await page.setViewportSize({ width: 375, height: 812 });
  await page.getByRole("button", { name: "Toggle sessions" }).click();
  assert.ok(await page.locator(".sidebar.is-open").isVisible());
  assert.equal(await page.evaluate(() => document.elementFromPoint(100, 130)?.closest(".sidebar") !== null), true);
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await page.screenshot({ path: join(tmpdir(), "playback-sidebar-mobile.png") });
  assert.deepEqual(errors, []);
  console.log("Sidebar check passed: drag in/out, keyboard move, hover create, group delete, mobile layout");
} finally {
  await browser.close();
}
