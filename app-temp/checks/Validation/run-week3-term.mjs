import { spawn } from "node:child_process";
import { readFile, writeFile } from "node:fs/promises";
const folder = "app-temp/data/validation/runs/2026-09-29-week3";
const termName = process.argv.includes("--symbols") ? "Microsoft Symbol Server" : "CPU";
if (!process.argv.includes("--live")) { console.log("Offline default. --live explicitly ranks and explains one real technical Week 3 term."); process.exit(0); }
try {
  const saved = JSON.parse(await readFile(folder + "/term-result.json", "utf8"));
  if (!process.argv.includes("--refresh")) {
    if (saved.term !== termName) { console.error("Saved result belongs to another term; an explicit --refresh is required for a new paid request."); process.exit(2); }
    console.log("Saved term result reused: " + saved.status); process.exit(saved.status === "completed" ? 0 : 2);
  }
} catch (error) { if (error.code !== "ENOENT") throw error; }
const child = spawn("dotnet", ["app-temp/data/validation/build/Playback.Checks.dll", "--week3-term-live", folder], {
  windowsHide: true, env: { ...process.env, PLAYBACK_WEEK3_LIVE: "yes", PLAYBACK_WEEK3_TERM: termName }, stdio: ["ignore", "pipe", "pipe"]
});
let log = "";
for (const stream of [child.stdout, child.stderr]) stream.on("data", bytes => {
  let safe = bytes.toString();
  for (const name of ["GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY"]) if (process.env[name]) safe = safe.split(process.env[name]).join("[redacted]");
  log += safe; process.stdout.write(safe);
});
child.on("close", async code => { await writeFile(folder + "/term-run.log", log); process.exitCode = code ?? 1; });
child.on("error", () => { console.error("Could not start bounded Week 3 term validation"); process.exitCode = 1; });
process.on("SIGINT", () => child.kill());
