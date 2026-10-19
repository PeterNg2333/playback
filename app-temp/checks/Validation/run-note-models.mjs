// Note model comparison on the Week 3 transcript. Offline by default (fixture writer, no network). `--live` calls
// Gemini 3.5 Flash-Lite on Vertex and the OpenRouter candidates once per two-minute step, or only the comma-separated
// `--models` list; saved live results are reused unless `--refresh` is given, so a rerun never pays again by accident.
import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

const live = process.argv.includes("--live");
const refresh = process.argv.includes("--refresh");
const folderArg = process.argv.indexOf("--folder");
const modelsArg = process.argv.indexOf("--models");
const today = new Date().toISOString().slice(0, 10);
const root = path.resolve("app-temp/data/validation/runs");
const folder = path.resolve(folderArg >= 0 ? process.argv[folderArg + 1] : path.join(root, `${today}-note-models`));
if (!folder.startsWith(root + path.sep)) throw new Error("Run folder must be inside app-temp/data/validation/runs");

const saved = path.join(folder, live ? "live-report.md" : "offline-report.md");
if (live && !refresh && existsSync(saved)) {
  console.log(readFileSync(saved, "utf8"));
  console.log(`Saved live evidence reused from ${folder}. No provider was called; pass --refresh to pay for a new run.`);
  process.exit(0);
}

const child = spawn("dotnet", ["run", "--project", "app-temp/checks/Playback.Checks.csproj", "--no-restore", "-p:UseAppHost=false",
  "-p:OutputPath=bin/verification/net10.0/", "--", live ? "--note-models-live" : "--note-models", folder], {
  windowsHide: true, stdio: ["ignore", "pipe", "pipe"], env: { ...process.env, PLAYBACK_NOTE_MODELS_LIVE: live ? "yes" : "no", PLAYBACK_NOTE_MODELS: modelsArg >= 0 ? process.argv[modelsArg + 1] : "" }
});
for (const stream of [child.stdout, child.stderr]) stream.on("data", bytes => {
  let safe = bytes.toString();
  for (const name of ["GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY"]) if (process.env[name]) safe = safe.split(process.env[name]).join("[redacted]");
  process.stdout.write(safe);
});
child.on("error", () => { console.error("Note model comparison could not start"); process.exitCode = 1; });
child.on("close", code => {
  if (existsSync(saved)) console.log(`\nReport: ${saved}`);
  process.exitCode = code ?? 1;
});
