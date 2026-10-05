import { spawn } from "node:child_process";
import { mkdir, appendFile, writeFile } from "node:fs/promises";
import path from "node:path";
const live = process.argv.includes("--live");
const folder = path.resolve("app-temp/data/validation/runs/2026-09-29-week3");
await mkdir(folder, { recursive: true });
const port = live ? 5081 : 5079;
try {
  await fetch(`http://127.0.0.1:${port}/api/health`, { signal: AbortSignal.timeout(1000) });
  throw Error(`Port ${port} is already in use`);
} catch (error) { if (error.message.includes("already in use")) throw error; }
const env = { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", PLAYBACK_MONGO_URI: "mongodb://127.0.0.1:27017",
  PLAYBACK_MONGO_DATABASE: "playback_e2e", PLAYBACK_AUTO_NOTES: "no", PLAYBACK_AUTO_TERMS: "no", PLAYBACK_AUTO_ORGANIZE: "no",
  PLAYBACK_OFFLINE_TEST: live ? "no" : "yes", PLAYBACK_PAUSE_EXTERNAL_ASR: live ? "yes" : "no", PLAYBACK_VALIDATION_PORT: live ? "5081" : "" };
// Playback audio/capture storage is relative to the conventional API output depth.
// Keep that depth even for the isolated validation binary.
const child = spawn("dotnet", ["app-temp/api/bin/week3-validation/net10.0/Playback.Api.dll"], { env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
await writeFile(path.join(folder, `${live ? "live" : "offline"}-server-process.json`), JSON.stringify({ pid: child.pid, port, startedAt: new Date().toISOString(), database: "playback_e2e", live }, null, 2));
let serial = Promise.resolve();
for (const stream of [child.stdout, child.stderr]) stream.on("data", bytes => {
  let safe = bytes.toString();
  for (const name of ["GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY"]) if (env[name]) safe = safe.split(env[name]).join("[redacted]");
  serial = serial.then(() => appendFile(path.join(folder, `${live ? "live" : "offline"}-server.log`), safe));
});
child.on("close", async code => { await serial; console.log(`Week 3 API exited ${code}`); process.exitCode = code ?? 1; });
child.on("error", () => { console.error("Could not start isolated Week 3 API"); process.exitCode = 1; });
process.on("SIGINT", () => child.kill()); process.on("SIGTERM", () => child.kill());
console.log(`Week 3 ${live ? "live" : "offline"} API on ${port}; automatic paid work disabled; test DB retained.`);
