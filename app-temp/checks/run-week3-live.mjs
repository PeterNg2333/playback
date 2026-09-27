import { spawn, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const audioFolder = path.join(root, "app-temp", "data", "test-audio", "Week 3");
const apiDll = path.join(root, "app-temp", "api", "bin", "Debug", "net10.0", "Playback.Api.dll");
const checksDll = path.join(root, "app-temp", "checks", "bin", "Debug", "net10.0", "Playback.Checks.dll");

if (!existsSync(path.join(audioFolder, "lecture_first_20min.m4a")))
  throw new Error(`Week 3 test audio is missing from ${audioFolder}`);
if (!process.env.GOOGLE_AI_STUDIO_API_KEY)
  throw new Error("Gemini credential is unavailable in the process environment");
if (!existsSync(apiDll) || !existsSync(checksDll))
  throw new Error("Build the API and checks projects before running the live test");

const env = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "Development",
  PLAYBACK_MONGO_DATABASE: "playback_e2e",
  PLAYBACK_AUTO_NOTES: "no",
  PLAYBACK_AUTO_TERMS: "no",
  PLAYBACK_OFFLINE_TEST: "no",
  PLAYBACK_PAUSE_EXTERNAL_ASR: "no",
  Logging__LogLevel__Microsoft: "Warning",
};

async function health() {
  try {
    const response = await fetch("http://127.0.0.1:5078/api/health", {
      signal: AbortSignal.timeout(2_000),
    });
    return response.ok ? await response.json() : { httpStatus: response.status };
  } catch {
    return null;
  }
}

async function waitForApi(child) {
  const deadline = Date.now() + 60_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) throw new Error("Live test API exited before becoming ready");
    const state = await health();
    if (state?.mongo) return state;
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error("Live test API or MongoDB did not become ready within 60 seconds");
}

function stopApi(child) {
  if (!child.pid || child.exitCode !== null) return;
  if (process.platform === "win32")
    spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
  else child.kill("SIGTERM");
}

if (await health())
  throw new Error("Port 5078 already has an API. Stop it before starting the isolated live test");

const api = spawn("dotnet", [apiDll], {
  cwd: root,
  env,
  stdio: "inherit",
});
process.once("SIGINT", () => { stopApi(api); process.exitCode = 130; });
process.once("SIGTERM", () => { stopApi(api); process.exitCode = 143; });

try {
  const state = await waitForApi(api);
  if (state.database !== "playback_e2e" || !state.gemini || !state.automaticAsr ||
      state.autoNotes || state.autoTerms)
    throw new Error("Live test API failed isolated database and provider preflight");
  console.log("Week 3 live API ready on isolated playback_e2e database.");
  const check = spawn("dotnet", [checksDll, "--week3-live", audioFolder],
    { cwd: root, env, stdio: "inherit" });
  const code = await new Promise((resolve, reject) => {
    check.once("error", reject);
    check.once("exit", (exitCode) => resolve(exitCode));
  });
  if (code !== 0) process.exitCode = code ?? 1;
} finally {
  stopApi(api);
}
