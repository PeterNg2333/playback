// Explicit bounded text-only live replay; normal invocation is offline and reusable.
import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
const live = process.argv.includes("--live");
const folderArg = process.argv.indexOf("--folder");
const folder = path.resolve(folderArg >= 0 ? process.argv[folderArg + 1] : "app-temp/data/validation/runs/2026-09-29-note-coverage");
const root = path.resolve("app-temp/data/validation/runs");
if (!folder.startsWith(root + path.sep)) throw new Error("Coverage run folder must be inside local validation/runs");
if (existsSync(path.join(folder, live ? "live-results.json" : "offline-results.json"))) {
  const saved = JSON.parse(readFileSync(path.join(folder, live ? "live-results.json" : "offline-results.json"), "utf8"));
  console.log(`Saved ${live ? "live" : "offline"} evidence retained: ${folder}; ${saved.final.unreferenced} parts still unreferenced. No new provider call.`);
  process.exit(saved.errors?.length ? 2 : 0);
}
const child = spawn("dotnet", ["app-temp/data/validation/note-coverage-build/Playback.Checks.dll", live ? "--note-coverage-live" : "--note-coverage-replay", folder], {
  windowsHide: true, stdio: ["ignore", "pipe", "pipe"], env: { ...process.env, PLAYBACK_NOTE_COVERAGE_LIVE: live ? "yes" : "no" }
});
for (const stream of [child.stdout, child.stderr]) stream.on("data", bytes => {
  let safe = bytes.toString();
  for (const name of ["GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY"]) if (process.env[name]) safe = safe.split(process.env[name]).join("[redacted]");
  process.stdout.write(safe);
});
child.on("error", () => { console.error("Coverage replay could not start"); process.exitCode = 1; });
child.on("close", code => { process.exitCode = code ?? 1; });
