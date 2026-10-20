// Load .env only into this process, then launch a stack isolated from the ordinary development database/audio.
import { spawn } from "node:child_process";
import { existsSync, mkdirSync } from "node:fs";
import path from "node:path";
const folder = path.resolve("app-temp/data/validation/runs/2026-10-20-wave1-p1-recording");
const review = process.argv.includes("--review");
const inspect = process.argv.includes("--inspect");
if (!review && !inspect && existsSync(path.join(folder, "session-id.txt"))) throw new Error("Recording evidence already exists; use --inspect for a read-only browser check");
mkdirSync(folder, { recursive: true });
const env = { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", PORT: "5088", PLAYBACK_MONGO_URI: "mongodb://127.0.0.1:27019",
  PLAYBACK_MONGO_DATABASE: "playback_e2e", Storage__DataPath: path.join(folder, "data"), Storage__KeyPath: path.join(folder, "keys"),
  PLAYBACK_AUTO_NOTES: inspect ? "no" : "yes", PLAYBACK_AUTO_ORGANIZE: inspect ? "no" : "yes", PLAYBACK_AUTO_TERMS: "no", PLAYBACK_AUTO_TRANSLATION: "no", PLAYBACK_OFFLINE_TEST: inspect ? "yes" : "no",
  PLAYBACK_PAUSE_EXTERNAL_ASR: inspect ? "yes" : "no", PLAYBACK_RESUME_SAVED_ASR: review ? "yes" : "no", PLAYBACK_NOTE_MODEL: "openrouter:qwen/qwen3.8-flash@none",
  PLAYBACK_WAVE1_RECORDING_LIVE: "yes", Logging__LogLevel__Default: "Warning" };
delete env.PLAYBACK_DEMO_PASSWORD;
delete env.PLAYBACK_PUBLIC_ORIGIN;
delete env.PLAYBACK_VALIDATION_PORT;
const safe = bytes => {
  let value = bytes.toString();
  for (const key of ["OPENROUTER_API_KEY", "GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY"]) if (env[key]) value = value.split(env[key]).join("[redacted]");
  return value;
};
const launch = args => {
  const child = spawn("dotnet", args, { env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
  for (const stream of [child.stdout, child.stderr]) stream.on("data", bytes => process.stdout.write(safe(bytes)));
  return child;
};
try { await fetch("http://127.0.0.1:5088/api/health", { signal: AbortSignal.timeout(1000) }); throw new Error("Test port already in use"); }
catch (error) { if (error.message === "Test port already in use") throw error; }
const api = launch(["app-temp/api/bin/wave1-fixed/net10.0/Playback.Api.dll"]);
try {
  let ready = false;
  for (let i = 0; i < 60 && api.exitCode === null; i++) {
    try {
      const r = await fetch("http://127.0.0.1:5088/api/health", { signal: AbortSignal.timeout(2000) });
      const health = await r.json();
      if (health.database === "playback_e2e" && health.mongo) { ready = true; break; }
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  if (!ready) throw new Error("Isolated API did not become ready");
  process.exitCode = 0;
  if (!inspect) {
    const check = launch(["app-temp/checks/bin/wave1-fixed/net10.0/Playback.Checks.dll", review ? "--wave1-review" : "--wave1-recording", folder]);
    process.exitCode = await new Promise((resolve, reject) => { check.on("error", reject); check.on("exit", code => resolve(code ?? 1)); });
  }
  if (process.exitCode === 0) {
    const browser = spawn(process.execPath, ["app-temp/web/test/wave1-live-browser.check.mjs"], { env, windowsHide: true, stdio: "inherit" });
    process.exitCode = await new Promise((resolve, reject) => { browser.on("error", reject); browser.on("exit", code => resolve(code ?? 1)); });
  }
} finally {
  if (api.exitCode === null) api.kill();
}
