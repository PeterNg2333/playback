// Local review only. All automatic external work is disabled; no containers are started.
import { spawn } from "node:child_process";
import { appendFile, writeFile } from "node:fs/promises";
const folder = "app-temp/data/validation/runs/2026-09-29-note-coverage";
try {
  const response = await fetch("http://127.0.0.1:5081/api/health", { signal: AbortSignal.timeout(1000) });
  if (response.ok) throw new Error("Review API port 5081 is occupied");
} catch (e) { if (e.message.includes("occupied")) throw e; }
const env = { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", PLAYBACK_MONGO_URI: "mongodb://127.0.0.1:27017",
  PLAYBACK_MONGO_DATABASE: "playback_e2e", PLAYBACK_VALIDATION_PORT: "5081", PLAYBACK_AUTO_NOTES: "no", PLAYBACK_AUTO_TERMS: "no",
  PLAYBACK_AUTO_ORGANIZE: "no", PLAYBACK_PAUSE_EXTERNAL_ASR: "yes", PLAYBACK_RESUME_SAVED_ASR: "no" };
const child = spawn("dotnet", ["app-temp/api/bin/note-coverage-validation/net10.0/Playback.Api.dll"], {
  env, windowsHide: true, stdio: ["ignore", "pipe", "pipe"] });
await writeFile(`${folder}/review-process.json`, JSON.stringify({ pid: child.pid, port: 5081, database: "playback_e2e", startedAt: new Date().toISOString() }, null, 2));
for (const stream of [child.stdout, child.stderr]) stream.on("data", bytes => {
  let safe = bytes.toString();
  for (const name of ["GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY"]) if (env[name]) safe = safe.split(env[name]).join("[redacted]");
  void appendFile(`${folder}/review-server.log`, safe);
});
child.on("close", code => { console.log(`Coverage review API exited ${code}`); process.exitCode = code ?? 1; });
child.on("error", () => { console.error("Coverage review API could not start"); process.exitCode = 1; });
process.on("SIGINT", () => child.kill()); process.on("SIGTERM", () => child.kill());
console.log("Coverage review API on 5081, local test DB; automatic provider work disabled.");
