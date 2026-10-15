// Explicit local Docker opt-in: synthetic credentials, isolated MongoDB and no provider traffic.
// Run after `docker build --platform linux/amd64 -t playback:cloud-run .`.
import assert from "node:assert/strict";
import { createHash, randomUUID } from "node:crypto";
import { spawnSync } from "node:child_process";
import { mkdir } from "node:fs/promises";
import { chromium, request } from "playwright-core";

const image = process.env.PLAYBACK_DOCKER_IMAGE || "playback:cloud-run";
const port = Number(process.env.PLAYBACK_DOCKER_TEST_PORT || 8088);
const base = `http://127.0.0.1:${port}`;
const prefix = `playback-cloud-check-${randomUUID().slice(0, 8)}`;
const network = `${prefix}-net`;
const mongo = `${prefix}-mongo`;
const app = `${prefix}-app`;
const secureApp = `${prefix}-secure`;
const password = randomUUID();
const env = {
  ...process.env,
  PLAYBACK_DEMO_PASSWORD: password,
};
let browser, client, anonymous, other;
const created = [];
let networkCreated = false;

function docker(args, extra = {}) {
  const result = spawnSync("docker", args, {
    encoding: "utf8",
    windowsHide: true,
    env,
    timeout: 120_000,
    ...extra,
  });
  if (result.status !== 0)
    throw new Error(
      `Docker ${args[0]} failed: ${result.stderr || result.error}`,
    );
  return result.stdout.trim();
}
async function ready() {
  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      const response = await fetch(`${base}/healthz`, {
        signal: AbortSignal.timeout(1500),
      });
      if (response.ok) return;
    } catch {
      /* Wait for startup, without substituting a successful response. */
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  const name = created.includes(secureApp) ? secureApp : app;
  throw new Error(
    `Container did not become ready:\n${docker(["logs", "--tail", "30", name])}`,
  );
}
async function login(
  client,
  suppliedPassword = password,
  account = "docker-check",
) {
  const page = await client.get(`${base}/login`);
  assert.equal(page.status(), 200);
  const token = (await page.text()).match(
    /name="__RequestVerificationToken" value="([^"]+)"/,
  )[1];
  return client.post(`${base}/login`, {
    headers: { Origin: base },
    form: {
      account,
      password: suppliedPassword,
      __RequestVerificationToken: token,
    },
    maxRedirects: 0,
  });
}
async function csrf(client) {
  const state = await client.storageState();
  return state.cookies.find((cookie) => cookie.name === "Playback.Csrf")?.value;
}

try {
  assert.equal(
    docker([
      "run",
      "--rm",
      "--entrypoint",
      "sh",
      image,
      "-c",
      "id -u; test ! -f /app/.env; test -f /app/wwwroot/index.html; test -f /app/Resources/silero_vad.onnx",
    ]),
    "1654",
  );
  const unconfigured = spawnSync(
    "docker",
    ["run", "--rm", "--network", "none", image],
    {
      encoding: "utf8",
      windowsHide: true,
      timeout: 20_000,
    },
  );
  assert.notEqual(unconfigured.status, 0);
  assert.match(
    unconfigured.stderr + unconfigured.stdout,
    /PLAYBACK_DEMO_PASSWORD/,
  );
  console.log(
    "PASS: image contains UI and VAD resources, runs as non-root, rejects missing credentials",
  );

  // Docker Desktop must route the published browser port; provider traffic is
  // disabled by offline mode and no provider credentials enter these containers.
  docker(["network", "create", network]);
  networkCreated = true;
  docker([
    "run",
    "-d",
    "--name",
    mongo,
    "--network",
    network,
    "--tmpfs",
    "/data/db",
    "mongo:8.0.32-noble",
  ]);
  created.push(mongo);
  const appArgs = [
    "run",
    "-d",
    "--name",
    app,
    "--network",
    network,
    "-p",
    `127.0.0.1:${port}:9090`,
    "-e",
    "PORT=9090",
    "-e",
    "PLAYBACK_DEMO_PASSWORD",
    "-e",
    `PLAYBACK_PUBLIC_ORIGIN=${base}`,
    "-e",
    `PLAYBACK_MONGO_URI=mongodb://${mongo}:27017`,
    "-e",
    "PLAYBACK_MONGO_DATABASE=playback_e2e",
    "-e",
    "PLAYBACK_OFFLINE_TEST=yes",
    image,
  ];
  docker(appArgs);
  created.push(app);
  await ready();
  client = await request.newContext({ timeout: 15_000 });
  anonymous = await request.newContext({ timeout: 15_000 });
  assert.equal(
    (await anonymous.get(`${base}/`, { maxRedirects: 0 })).status(),
    302,
  );
  for (const path of [
    "/api/health",
    "/api/sessions",
    "/api/chunks/unknown/audio",
    "/api/sessions/unknown/audio/segments/0",
  ]) {
    assert.equal((await anonymous.get(base + path)).status(), 401, path);
  }
  assert.equal(
    (
      await anonymous.post(`${base}/api/sessions/unknown/ask/stream`, {
        headers: { Origin: base },
      })
    ).status(),
    401,
  );
  assert.equal(
    (
      await anonymous.post(`${base}/login`, {
        headers: { Origin: base },
        form: { account: "docker-check", password },
      })
    ).status(),
    403,
  );
  assert.equal((await login(client, "wrong-password")).status(), 401);
  assert.equal((await login(client, password, "bad username")).status(), 401);
  assert.equal(
    docker([
      "exec",
      mongo,
      "mongosh",
      "--quiet",
      "--eval",
      'db.getSiblingDB("playback_e2e").users.countDocuments({})',
    ]),
    "0",
  );
  assert.equal((await login(client)).status(), 302);
  const registeredAt = docker([
    "exec",
    mongo,
    "mongosh",
    "--quiet",
    "--eval",
    'db.getSiblingDB("playback_e2e").users.findOne({_id:"docker-check"}).CreatedAt.toISOString()',
  ]);
  let token = await csrf(client);
  assert.ok(token);
  assert.equal((await client.get(`${base}/api/health`)).status(), 200);
  for (let attempt = 0; attempt < 20; attempt++) {
    const health = await (await client.get(`${base}/api/health`)).json();
    if (health.mongo) break;
    if (attempt === 19)
      throw new Error("Isolated MongoDB did not become ready");
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  console.log(
    "PASS: anonymous API/audio/stream requests blocked; login requires valid credentials and CSRF",
  );

  const headers = { Origin: base, "X-CSRF-TOKEN": token };
  assert.equal(
    (
      await client.post(`${base}/api/sessions`, {
        headers: { Origin: base },
        data: { title: "Rejected" },
      })
    ).status(),
    403,
  );
  assert.equal(
    (
      await client.post(`${base}/api/sessions`, {
        headers: { ...headers, "X-CSRF-TOKEN": "invalid" },
        data: { title: "Rejected" },
      })
    ).status(),
    403,
  );
  assert.equal(
    (
      await client.post(`${base}/api/sessions`, {
        headers: { ...headers, Origin: "https://evil.example" },
        data: { title: "Rejected" },
      })
    ).status(),
    403,
  );
  assert.equal(
    (
      await client.post(`${base}/api/sessions`, {
        headers: { "X-CSRF-TOKEN": token },
        data: { title: "Rejected" },
      })
    ).status(),
    403,
  );
  assert.equal(
    (
      await client.get(`${base}/api/sessions`, {
        headers: { Origin: "https://evil.example" },
      })
    ).status(),
    403,
  );
  assert.equal(
    (
      await client.get(`${base}/api/sessions`, {
        headers: { "Sec-Fetch-Site": "cross-site" },
      })
    ).status(),
    403,
  );
  const preflight = await anonymous.fetch(`${base}/api/sessions`, {
    method: "OPTIONS",
    headers: {
      Origin: "https://evil.example",
      "Access-Control-Request-Method": "POST",
    },
  });
  assert.equal(preflight.status(), 403);
  assert.equal(preflight.headers()["access-control-allow-origin"], undefined);
  const create = await client.post(`${base}/api/sessions`, {
    headers,
    data: { title: "Docker security check" },
  });
  assert.equal(create.status(), 200);
  const session = await create.json();
  assert.equal(
    (await (await client.get(`${base}/api/health`)).json()).localCapture,
    false,
  );
  assert.equal(
    (
      await client.put(`${base}/api/sessions/${session.id}/languages`, {
        headers,
        data: { asrLanguage: "auto", noteLanguage: "en" },
      })
    ).status(),
    200,
  );
  assert.equal(
    (
      await client.post(`${base}/api/sessions/${session.id}/notes`, {
        headers,
        data: { markdown: "# Docker note\nRetained after restart." },
      })
    ).status(),
    200,
  );
  assert.equal(
    (
      await client.post(`${base}/api/chunks`, {
        headers: { Origin: base },
        multipart: {
          file: {
            name: "test.wav",
            mimeType: "audio/wav",
            buffer: Buffer.alloc(44),
          },
        },
      })
    ).status(),
    403,
  );
  const wav = Buffer.alloc(44 + 3200);
  wav.write("RIFF", 0);
  wav.writeUInt32LE(wav.length - 8, 4);
  wav.write("WAVEfmt ", 8);
  wav.writeUInt32LE(16, 16);
  wav.writeUInt16LE(1, 20);
  wav.writeUInt16LE(1, 22);
  wav.writeUInt32LE(16000, 24);
  wav.writeUInt32LE(32000, 28);
  wav.writeUInt16LE(2, 32);
  wav.writeUInt16LE(16, 34);
  wav.write("data", 36);
  wav.writeUInt32LE(3200, 40);
  const multipart = {
    sessionId: session.id,
    sourceId: "microphone",
    sequence: "0",
    startMs: "0",
    endMs: "100",
    sha256: createHash("sha256").update(wav).digest("hex"),
    file: { name: "silence.wav", mimeType: "audio/wav", buffer: wav },
  };
  const upload = await client.post(`${base}/api/chunks`, {
    headers,
    multipart,
  });
  assert.equal(upload.status(), 202);
  const chunk = await upload.json();
  const repeat = await client.post(`${base}/api/chunks`, {
    headers,
    multipart,
  });
  assert.equal((await repeat.json()).id, chunk.id);
  assert.deepEqual(
    await (await client.get(`${base}/api/chunks/${chunk.id}/audio`)).body(),
    wav,
  );
  const range = await client.get(`${base}/api/chunks/${chunk.id}/audio`, {
    headers: { Range: "bytes=0-43" },
  });
  assert.equal(range.status(), 206);
  assert.deepEqual(await range.body(), wav.subarray(0, 44));
  assert.equal((await client.get(`${base}/api/not-a-route`)).status(), 404);
  assert.match(
    (await client.get(`${base}/api/not-a-route`)).headers()["content-type"],
    /json/,
  );
  assert.equal(
    (
      await client.post(
        `${base}/api/testing/sessions/${session.id}/transcripts`,
        { headers, data: {} },
      )
    ).status(),
    404,
  );
  const index = await client.get(`${base}/`, {
    headers: { Accept: "text/html" },
  });
  const asset = (await index.text()).match(/src="([^"]+\.js)"/)[1];
  assert.equal((await client.get(base + asset)).status(), 200);

  // Both users share the test password; identity comes from their separate login cookies.
  other = await request.newContext({ timeout: 15_000 });
  assert.equal((await login(other, password, "docker-other")).status(), 302);
  assert.equal(
    docker([
      "exec",
      mongo,
      "mongosh",
      "--quiet",
      "--eval",
      'db.getSiblingDB("playback_e2e").users.countDocuments({})',
    ]),
    "2",
  );
  console.log(
    "PASS: new usernames are created in MongoDB without an account allowlist; failed logins create no user",
  );
  const otherHeaders = { Origin: base, "X-CSRF-TOKEN": await csrf(other) };
  assert.deepEqual(await (await other.get(`${base}/api/sessions`)).json(), []);
  const group = await (
    await client.post(`${base}/api/groups`, {
      headers,
      data: { name: "Private group" },
    })
  ).json();
  assert.deepEqual(await (await other.get(`${base}/api/groups`)).json(), []);
  const otherGroup = await (
    await other.post(`${base}/api/groups`, {
      headers: otherHeaders,
      data: { name: "Other group" },
    })
  ).json();
  const otherSession = await (
    await other.post(`${base}/api/sessions`, {
      headers: otherHeaders,
      data: { title: "Other user notes", ownerId: "docker-check" },
    })
  ).json();
  assert.equal(
    (
      await other.post(`${base}/api/sessions/${otherSession.id}/notes`, {
        headers: otherHeaders,
        data: { markdown: "# Other user's private note" },
      })
    ).status(),
    200,
  );
  const otherList = await (await other.get(`${base}/api/sessions`)).json();
  assert.deepEqual(
    otherList.map((item) => item.id),
    [otherSession.id],
  );
  assert.equal(
    otherList[0].ownerId,
    "docker-other",
    "A submitted ownerId must not override the login identity",
  );
  assert.deepEqual(
    (await (await client.get(`${base}/api/sessions`)).json()).map(
      (item) => item.id,
    ),
    [session.id],
  );
  const conversation = await (
    await client.post(`${base}/api/sessions/${session.id}/conversations`, {
      headers,
    })
  ).json();
  for (const suffix of [
    "",
    "/sync",
    "/notes",
    "/notes/history",
    "/notes/1",
    "/notes/1/recovery",
    "/notes/edits",
    "/notes/coverage",
    "/conversations",
    `/conversations/${conversation.id}`,
    "/activity",
    "/audio/segments/0",
  ]) {
    assert.equal(
      (await other.get(`${base}/api/sessions/${session.id}${suffix}`)).status(),
      404,
      `Foreign session read: ${suffix}`,
    );
  }
  assert.equal(
    (
      await other.get(`${base}/api/chunks/${chunk.id}/audio`, {
        headers: { Range: "bytes=0-43" },
      })
    ).status(),
    404,
  );
  assert.equal(
    (await other.get(`${base}/api/groups/${group.id}/flow`)).status(),
    404,
  );
  assert.equal(
    (
      await other.get(
        `${base}/api/groups/${otherGroup.id}/flow?sessionId=${session.id}`,
      )
    ).status(),
    404,
  );
  assert.equal(
    (
      await other.put(`${base}/api/groups/${group.id}`, {
        headers: otherHeaders,
        data: { name: "Hijacked" },
      })
    ).status(),
    404,
  );
  assert.equal(
    (
      await other.delete(`${base}/api/groups/${group.id}`, {
        headers: otherHeaders,
      })
    ).status(),
    404,
  );
  assert.equal(
    (
      await other.post(`${base}/api/sessions`, {
        headers: otherHeaders,
        data: { title: "Hijacked", groupId: group.id },
      })
    ).status(),
    404,
  );
  assert.equal(
    (
      await other.put(`${base}/api/sessions/${otherSession.id}/group`, {
        headers: otherHeaders,
        data: { groupId: group.id },
      })
    ).status(),
    404,
  );
  for (const suffix of [
    "/notes",
    "/notes/generate",
    "/notes/repair",
    "/notes/organize",
    "/notes/1/restore",
    "/notes/1/recovery",
    "/materials",
    "/conversations",
    "/ask",
    "/ask/stream",
    "/chunks/retry",
  ]) {
    assert.equal(
      (
        await other.post(`${base}/api/sessions/${session.id}${suffix}`, {
          headers: otherHeaders,
          data: {},
        })
      ).status(),
      404,
      `Foreign session write: ${suffix}`,
    );
  }
  assert.equal(
    (
      await other.delete(`${base}/api/sessions/${session.id}`, {
        headers: otherHeaders,
      })
    ).status(),
    404,
  );
  assert.equal(
    (
      await other.post(`${base}/api/chunks`, {
        headers: otherHeaders,
        multipart,
      })
    ).status(),
    404,
  );
  assert.equal(
    (
      await other.post(`${base}/api/capture/start`, {
        headers: otherHeaders,
        data: { sessionId: session.id },
      })
    ).status(),
    404,
  );
  assert.equal(
    (
      await client.get(`${base}/api/sessions/${otherSession.id}/notes`)
    ).status(),
    404,
  );
  assert.match(
    JSON.stringify(
      await (
        await client.get(`${base}/api/sessions/${session.id}/notes`)
      ).json(),
    ),
    /Retained after restart/,
  );
  assert.match(
    JSON.stringify(
      await (
        await other.get(`${base}/api/sessions/${otherSession.id}/notes`)
      ).json(),
    ),
    /Other user's private note/,
  );
  // Only this run's temporary MongoDB: legacy records remain unowned and hidden.
  const legacyId = "00000000000000000000000000000001";
  docker([
    "exec",
    mongo,
    "mongosh",
    "--quiet",
    "--eval",
    `db.getSiblingDB('playback_e2e').sessions.insertOne({_id:'${legacyId}',Title:'Unassigned legacy note',CreatedAt:new Date()})`,
  ]);
  assert.equal(
    (await client.get(`${base}/api/sessions/${legacyId}`)).status(),
    404,
  );
  assert.equal(
    (await other.get(`${base}/api/sessions/${legacyId}`)).status(),
    404,
  );
  assert.equal(
    (await (await client.get(`${base}/api/sessions`)).json()).length,
    1,
  );
  assert.equal(
    (await (await other.get(`${base}/api/sessions`)).json()).length,
    1,
  );
  console.log(
    "PASS: separate users, notes, groups, chats, sources, audio and writes; forged ownership and unassigned legacy records blocked",
  );
  console.log(
    "PASS: same-origin GET/POST/PUT and CSRF enforcement, real MongoDB writes, bundled frontend, production-only routes",
  );

  docker(["restart", app]);
  await ready();
  assert.equal((await client.get(`${base}/api/sessions`)).status(), 200);
  const notes = await (
    await client.get(`${base}/api/sessions/${session.id}/notes`)
  ).json();
  assert.match(JSON.stringify(notes), /Retained after restart/);
  assert.deepEqual(
    await (await client.get(`${base}/api/chunks/${chunk.id}/audio`)).body(),
    wav,
  );
  assert.equal(
    (await other.get(`${base}/api/sessions/${session.id}/notes`)).status(),
    404,
  );
  assert.match(
    JSON.stringify(
      await (
        await other.get(`${base}/api/sessions/${otherSession.id}/notes`)
      ).json(),
    ),
    /Other user's private note/,
  );
  console.log(
    "PASS: login, MongoDB notes and saved WAV survive a container restart; retry identity and audio ranges work",
  );

  browser = await chromium.launch({
    headless: true,
    executablePath:
      process.env.PLAYBACK_BROWSER ||
      "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  });
  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
  });
  const page = await context.newPage();
  const errors = [];
  page.on("pageerror", (error) => errors.push(error.message));
  await page.goto(base);
  await page.getByLabel("Username").fill("docker-check");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page.getByRole("button", { name: "Sign out", exact: true }).waitFor();
  await page
    .getByRole("heading", { name: "Docker security check", exact: true })
    .waitFor();
  await page.getByText("Recording unavailable", { exact: true }).waitFor();
  assert.equal(
    await page
      .getByRole("button", { name: "Start recording", exact: true })
      .count(),
    0,
  );
  const cookies = await context.cookies();
  assert.equal(
    cookies.find((cookie) => cookie.name === "Playback.Session").httpOnly,
    true,
  );
  assert.equal(
    cookies.find((cookie) => cookie.name === "Playback.Session").sameSite,
    "Strict",
  );
  // Exercise the shipped API client through a real UI write, including token forwarding.
  await page.getByRole("button", { name: "New group", exact: true }).click();
  await page.getByLabel("Group name", { exact: true }).fill("Browser check");
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Save", exact: true })
    .click();
  await page
    .getByRole("button", { name: "Collapse Browser check", exact: true })
    .waitFor();
  await mkdir("output/playwright", { recursive: true });
  await page.screenshot({ path: "output/playwright/docker-workspace.png" });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: "output/playwright/docker-mobile.png" });
  await page.getByRole("button", { name: "Sign out", exact: true }).click();
  await page.getByRole("button", { name: "Sign in", exact: true }).waitFor();
  assert.equal(
    (await context.request.get(`${base}/api/sessions`)).status(),
    401,
  );
  await page.screenshot({ path: "output/playwright/docker-login.png" });
  await page.getByLabel("Username").fill("docker-other");
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await page
    .getByRole("heading", { name: "Other user notes", exact: true })
    .waitFor();
  assert.equal(
    await page
      .getByRole("heading", { name: "Docker security check", exact: true })
      .count(),
    0,
  );
  assert.equal(
    await page.getByText("Retained after restart.", { exact: false }).count(),
    0,
  );
  assert.deepEqual(errors, []);
  console.log(
    "PASS: real browser login, desktop/mobile workspace, logout and HttpOnly/SameSite cookies",
  );

  assert.equal(
    (
      await client.post(`${base}/api/auth/logout`, {
        headers: { Origin: base },
      })
    ).status(),
    403,
  );
  // A browser page visit may rotate the token; this separate API context retains its own valid token.
  token = await csrf(client);
  assert.equal(
    (
      await client.post(`${base}/api/auth/logout`, {
        headers: { Origin: base, "X-CSRF-TOKEN": token },
      })
    ).status(),
    204,
  );
  assert.equal((await client.get(`${base}/api/sessions`)).status(), 401);
  let limited = false;
  for (let attempt = 0; attempt < 12; attempt++) {
    if ((await login(anonymous, "wrong-password")).status() === 429) {
      limited = true;
      break;
    }
  }
  assert.ok(limited, "Login attempts must be rate limited");
  console.log("PASS: logout CSRF enforcement and login rate limit");

  docker(["stop", app]);
  docker([
    "run",
    "-d",
    "--name",
    secureApp,
    "--network",
    network,
    "-p",
    `127.0.0.1:${port}:8080`,
    "-e",
    "PLAYBACK_DEMO_PASSWORD",
    "-e",
    "PLAYBACK_PUBLIC_ORIGIN=https://playback.example",
    "-e",
    `PLAYBACK_MONGO_URI=mongodb://${mongo}:27017`,
    "-e",
    "PLAYBACK_MONGO_DATABASE=playback_e2e",
    "-e",
    "PLAYBACK_OFFLINE_TEST=yes",
    image,
  ]);
  created.push(secureApp);
  await ready();
  const secureLogin = await anonymous.get(`${base}/login`);
  assert.equal(secureLogin.status(), 200);
  assert.match(secureLogin.headers()["set-cookie"], /secure/i);
  assert.match(secureLogin.headers()["set-cookie"], /httponly/i);
  assert.match(secureLogin.headers()["set-cookie"], /samesite=strict/i);
  const secureToken = (await secureLogin.text()).match(
    /name="__RequestVerificationToken" value="([^"]+)"/,
  )[1];
  const antiforgeryCookie = secureLogin.headers()["set-cookie"].split(";")[0];
  const secureSignIn = await anonymous.post(`${base}/login`, {
    headers: { Origin: "https://playback.example", Cookie: antiforgeryCookie },
    form: {
      account: "docker-check",
      password,
      __RequestVerificationToken: secureToken,
    },
    maxRedirects: 0,
  });
  assert.equal(secureSignIn.status(), 302);
  assert.equal(
    docker([
      "exec",
      mongo,
      "mongosh",
      "--quiet",
      "--eval",
      'db.getSiblingDB("playback_e2e").users.findOne({_id:"docker-check"}).CreatedAt.toISOString()',
    ]),
    registeredAt,
    "Repeated login must reuse the stored user",
  );
  const sessionCookie = secureSignIn
    .headersArray()
    .find(
      (header) =>
        header.name.toLowerCase() === "set-cookie" &&
        header.value.startsWith("Playback.Session="),
    );
  assert.match(sessionCookie.value, /secure/i);
  assert.match(sessionCookie.value, /httponly/i);
  assert.match(sessionCookie.value, /samesite=strict/i);
  console.log(
    "PASS: HTTPS production configuration issues secure session/antiforgery cookies behind HTTP TLS termination",
  );
} finally {
  await browser?.close();
  await client?.dispose();
  await anonymous?.dispose();
  await other?.dispose();
  // Only containers created by this run. MongoDB data is in this run's disposable tmpfs.
  for (const name of created.reverse()) docker(["rm", "-f", "-v", name]);
  if (networkCreated) docker(["network", "rm", network]);
}
