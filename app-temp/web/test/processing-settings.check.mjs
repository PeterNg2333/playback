// Real local browser, fixture API only. Starts its own Vite listener; no API host or database.
import assert from "node:assert/strict";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";
import { chromium } from "playwright-core";

const web = fileURLToPath(new URL("..", import.meta.url));
const preview = path.join(web, "output/phase2");
const artifacts = path.resolve(
  web,
  "../../output/playwright/processing-settings",
);
await mkdir(preview, { recursive: true });
await mkdir(artifacts, { recursive: true });
const defaults = {
  jevEnabled: true,
  probabilityThreshold: 0.7,
  confidenceThreshold: 0.5,
  checkIntervalSeconds: 10,
  maxPendingWaitSeconds: 30,
  silenceBoundarySeconds: 2,
  boundarySettleSeconds: 2,
  combineQuiet: true, maxCombinedSeconds: 60, organizationEnabled: true,
  organizationChangedRevisions: 3, organizationMaxDirtyAgeSeconds: 120,
  organizationCooldownSeconds: 60, organizationScope: "affected-topics", notePresentation: "numbered-hierarchy",
};
let workspace = { revision: 0, overrides: {} },
  session = { revision: 0, overrides: {} };
const apply = (base, overrides) => ({
  ...base,
  ...Object.fromEntries(
    Object.entries(overrides).filter(([, value]) => value != null),
  ),
});
function view(scope) {
  const inherited =
    scope === "workspace" ? defaults : apply(defaults, workspace.overrides);
  const record = scope === "workspace" ? workspace : session;
  return {
    schemaVersion: 1,
    ...record,
    inheritedRevision: scope === "workspace" ? 0 : workspace.revision,
    effectiveRevision: `${workspace.revision}:${scope === "workspace" ? 0 : session.revision}`,
    inherited,
    effective: apply(inherited, record.overrides),
  };
}
await writeFile(
  path.join(preview, "index.html"),
  '<div id="root"></div><script type="module" src="./entry.tsx"></script>',
);
await writeFile(
  path.join(preview, "entry.tsx"),
  `
import React, { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ProcessingSettingsDialog } from '../../src/features/notes/ProcessingSettingsDialog';
import { NoteAdmissionStatus } from '../../src/features/notes/NoteAdmissionStatus';
import '../../src/styles/app.css';
const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
const status = ${JSON.stringify({
    processingSettings: view("session"),
    noteAdmission: {
      status: "wait",
      admissionReason: "continuation",
      decision: "wait",
      policyRevision: "0:0",
      attempts: 0,
      flushRequested: false,
      gateFailure: "",
      firstPendingAt: new Date(Date.now() - 18000).toISOString(),
    },
  })};
function Preview() { const [open, setOpen] = useState(true); return <>
  <button onClick={() => setOpen(true)}>Open settings</button>
  <NoteAdmissionStatus session={status as any} />
  {open && <ProcessingSettingsDialog sessionId="fixture" onClose={() => setOpen(false)} />}
</>; }
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={client}><Preview /></QueryClientProvider>);
`,
);
const server = await createServer({
  root: web,
  configFile: path.join(web, "vite.config.ts"),
  configLoader: "runner",
  server: { host: "127.0.0.1", port: 0, open: false },
});
let browser;
const errors = [],
  writes = [];
try {
  await server.listen();
  const origin = `http://127.0.0.1:${server.httpServer.address().port}`;
  browser = await chromium.launch({
    headless: true,
    executablePath:
      "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
    args: ["--disable-background-networking"],
  });
  const context = await browser.newContext({
    viewport: { width: 1000, height: 900 },
    serviceWorkers: "block",
  });
  await context.route("**/*", async (route) => {
    const url = new URL(route.request().url());
    if (url.origin !== origin) return route.abort();
    if (!url.pathname.startsWith("/api/")) return route.continue();
    const scope =
      url.pathname === "/api/settings/processing" ? "workspace" : "session";
    if (
      ![
        "/api/settings/processing",
        "/api/sessions/fixture/processing-settings",
      ].includes(url.pathname)
    )
      throw new Error(`Unexpected fixture request ${url.pathname}`);
    if (route.request().method() === "PUT") {
      const body = route.request().postDataJSON(),
        record = scope === "workspace" ? workspace : session;
      writes.push({ scope, body });
      if (
        body.expectedRevision !== record.revision ||
        (scope === "session" &&
          body.expectedInheritedRevision !== workspace.revision)
      )
        return route.fulfill({
          status: 409,
          json: { error: "Processing settings changed; reload before saving" },
        });
      const next = { revision: record.revision + 1, overrides: body.overrides };
      if (scope === "workspace") workspace = next;
      else session = next;
    }
    return route.fulfill({ json: view(scope) });
  });
  const page = await context.newPage();
  page.setDefaultTimeout(10000);
  page.on("pageerror", (error) => errors.push(error.message));
  await page.goto(origin + "/output/phase2/index.html");
  const dialog = page.getByRole("dialog");
  const maximum = page.getByLabel("Maximum pending wait", { exact: true });
  await maximum.waitFor();
  assert.equal(await maximum.isDisabled(), true);
  assert.equal(await maximum.inputValue(), "30");
  await dialog
    .getByRole("checkbox", { name: "Override", exact: true })
    .nth(1)
    .check();
  await maximum.fill("18");
  await dialog
    .getByRole("checkbox", { name: "Override", exact: true })
    .nth(2)
    .check();
  await dialog
    .getByRole("checkbox", {
      name: "Ask Jev whether a passage is complete before the deadline",
    })
    .uncheck();
  await dialog
    .getByRole("button", { name: "Save settings", exact: true })
    .click();
  await page.waitForFunction(
    () => !document.querySelector("dialog button:last-child")?.disabled,
  );
  assert.equal(session.overrides.jevEnabled, false);
  assert.equal(session.overrides.maxPendingWaitSeconds, 18);
  assert.equal(session.overrides.checkIntervalSeconds ?? null, null);
  // Another tab saves while this draft is open. The conflict must retain the local edit.
  session = {
    revision: 2,
    overrides: { jevEnabled: false, maxPendingWaitSeconds: 22 },
  };
  await maximum.fill("16");
  await dialog
    .getByRole("button", { name: "Save settings", exact: true })
    .click();
  await dialog
    .getByRole("alert")
    .filter({ hasText: "Your changes are retained" })
    .waitFor();
  assert.equal(await maximum.inputValue(), "16");
  await dialog
    .getByRole("button", { name: "Reload saved settings", exact: true })
    .click();
  await page.waitForFunction(
    () =>
      document.querySelector("#processing-maxPendingWaitSeconds")?.value ===
      "22",
  );
  await dialog
    .getByRole("button", { name: "Use workspace defaults", exact: true })
    .click();
  assert.equal(await maximum.isDisabled(), true);
  assert.equal(await maximum.inputValue(), "30");
  await dialog
    .getByRole("button", { name: "Save settings", exact: true })
    .click();
  await page.waitForFunction(
    () => !document.querySelector("dialog button:last-child")?.disabled,
  );
  assert.ok(Object.values(session.overrides).every((value) => value == null));
  await dialog.getByLabel("Apply to").selectOption("workspace");
  await page.waitForFunction(
    () =>
      !document.querySelector("#processing-maxPendingWaitSeconds")?.disabled,
  );
  await maximum.fill("9");
  assert.equal(
    await dialog
      .getByRole("button", { name: "Save settings", exact: true })
      .isDisabled(),
    true,
  );
  await maximum.fill("40");
  await dialog
    .getByRole("button", { name: "Save settings", exact: true })
    .click();
  await page.waitForFunction(
    () => !document.querySelector("dialog button:last-child")?.disabled,
  );
  assert.equal(workspace.overrides.maxPendingWaitSeconds, 40);
  await dialog.getByLabel("Organization cooldown", {exact: true}).fill("301");
  assert.equal(await dialog.getByRole("button", {name: "Save settings", exact: true}).isDisabled(), true);
  await dialog.getByLabel("Organization cooldown", {exact: true}).fill("15");
  assert.equal(await dialog.getByLabel("Changed revisions", {exact: true}).count(), 0);
  await dialog.getByLabel("Organize automatically", {exact: true}).uncheck();
  await dialog.getByLabel("Organization scope", {exact: true}).selectOption("document-outline");
  await dialog.getByLabel("Note presentation", {exact: true}).selectOption("headings");
  await dialog.getByRole("button", {name: "Save settings", exact: true}).click();
  await page.waitForFunction(() => !document.querySelector("dialog button:last-child")?.disabled);
  assert.equal(workspace.overrides.organizationEnabled, false);
  assert.equal(workspace.overrides.organizationScope, "document-outline");
  assert.equal(workspace.overrides.notePresentation, "headings");
  await dialog.getByLabel("Apply to").selectOption("session");
  await page.waitForFunction(
    () =>
      document.querySelector("#processing-maxPendingWaitSeconds")?.value ===
      "40",
  );
  // A narrow viewport keeps all controls inside the dialog and permits scrolling.
  await page.setViewportSize({ width: 390, height: 760 });
  await dialog.getByText("Advanced thresholds", { exact: true }).click();
  await dialog.screenshot({
    path: path.join(artifacts, "settings-mobile.png"),
  });
  assert.ok((await dialog.boundingBox()).width <= 390);
  await dialog.getByRole("button", { name: "Close", exact: true }).click();
  await page.getByRole("status").waitFor();
  assert.match(
    await page.getByRole("status").innerText(),
    /Waiting for continuation.*s \/ 30 s/,
  );
  assert.deepEqual(errors, []);
  await writeFile(
    path.join(artifacts, "result.json"),
    JSON.stringify(
      {
        passed: true,
        writes: writes.length,
        providerCalls: 0,
        databaseCalls: 0,
        checks: [
          "inheritance",
          "partial overrides",
          "explicit false",
          "conflict draft retention",
          "reload",
          "reset",
          "workspace defaults",
          "inline validation",
          "mobile",
          "countdown",
        ],
      },
      null,
      2,
    ),
  );
  console.log(
    "Processing settings browser checks passed: inheritance, partial overrides/false, conflict draft retention, reload/reset, workspace defaults, validation, mobile dialog and countdown (fixture API only)",
  );
} finally {
  await browser?.close();
  await server.close();
}
