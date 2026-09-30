import { spawn } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
const folder = path.resolve("app-temp/data/validation/runs/2026-09-29-week3");
if (!process.argv.includes("--live")) { console.log("Offline default. Explicit --live runs bounded Week 3 Jev/Gemini text checks; saved results are reused unless --refresh is supplied."); process.exit(0); }
await mkdir(folder, { recursive: true });
let saved;
try { saved = JSON.parse(await readFile(path.join(folder, "provider-results.json"), "utf8")); } catch {}
if (saved && !process.argv.includes("--refresh")) {
  const failed = (saved.errors?.length ?? 0) > 0 || !(saved.noteVersions?.length > 0);
  console.log(`${failed ? "Saved incomplete results; this is not a passing live test" : "Using saved provider results; content still requires review"}: ${folder}`);
  process.exit(failed ? 2 : 0);
}
const child = spawn("dotnet", ["app-temp/data/validation/build/Playback.Checks.dll", "--week3-live", folder], {
  windowsHide: true, stdio: ["ignore", "pipe", "pipe"], env: { ...process.env, PLAYBACK_WEEK3_LIVE: "yes" }
});
let log = "";
for (const stream of [child.stdout, child.stderr]) stream.on("data", bytes => {
  let safe = bytes.toString();
  for (const name of ["GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY"]) if (process.env[name]) safe = safe.split(process.env[name]).join("[redacted]");
  log += safe; process.stdout.write(safe);
});
child.on("error", () => { console.error("Could not start Week 3 checks"); process.exitCode = 1; });
child.on("close", async code => { await writeFile(path.join(folder, "provider-run.log"), log); process.exitCode = code ?? 1; });
process.on("SIGINT", () => child.kill());
