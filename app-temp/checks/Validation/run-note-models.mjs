// Note model comparison on the Week 3 transcript. Offline by default (fixture writer, no network). `--live` calls
// Gemini 3.5 Flash-Lite on Vertex and the OpenRouter candidates once per two-minute step, or only the comma-separated
// `--models` list; saved live results are reused unless `--refresh` is given, so a rerun never pays again by accident.
import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";

const live = process.argv.includes("--live");
const refresh = process.argv.includes("--refresh");
const folderArg = process.argv.indexOf("--folder");
const modelsArg = process.argv.indexOf("--models");
const option = (name, fallback) => {
  const index = process.argv.indexOf(name);
  if (index < 0) return fallback;
  if (!process.argv[index + 1] || process.argv[index + 1].startsWith("--")) throw new Error(`${name} needs a value`);
  return process.argv[index + 1];
};
const source = option("--source", "week3");
const minutes = option("--minutes", "20");
const script = option("--script", "raw");
const budget = option("--max-usd", "");
const resume = option("--resume-from", "");
if (budget && (!Number.isFinite(+budget) || +budget <= 0)) throw new Error("Budget must be positive USD");
if (!["week3", "cantonese-fixture"].includes(source)) throw new Error("Unknown replay source");
if (minutes !== "full" && (!/^\d+$/.test(minutes) || +minutes < 1 || +minutes > 180)) throw new Error("Minutes must be 1–180 or full");
if (!["raw", "zh-Hant"].includes(script)) throw new Error("Script must be raw or zh-Hant");
const today = new Date().toISOString().slice(0, 10);
const root = path.resolve("app-temp/data/validation/runs");
const folder = path.resolve(folderArg >= 0 ? process.argv[folderArg + 1] : path.join(root, `${today}-note-models`));
if (!folder.startsWith(root + path.sep)) throw new Error("Run folder must be inside app-temp/data/validation/runs");

const saved = path.join(folder, live ? "live-report.md" : "offline-report.md");
if (live && !refresh && existsSync(saved)) {
  const metadata = JSON.parse(readFileSync(path.join(folder, "live-results.json"), "utf8"));
  const expectedFile = source === "week3" ? "app-temp/data/test-audio/sampleAudio/transcript.txt" : "app-temp/checks/Fixtures/cantonese-lecture.txt";
  const text = readFileSync(expectedFile, "utf8").replace(/^\uFEFF/, "");
  const hash = createHash("sha256").update(text).digest("hex");
  const ends = text.split(/\r?\n/).map(x => x.trim()).filter(x => /^\d{1,3}:\d{2}(?::\d{2})?$/.test(x))
    .map(x => x.split(":").reduce((n, part) => n * 60 + +part, 0) * 1000);
  const end = Math.max(...ends);
  const span = minutes === "full" ? end : Math.min(end, +minutes * 60_000);
  const models = modelsArg >= 0 ? option("--models", "").split(",").map(x => x.trim()) : null;
  if (metadata.input.file !== expectedFile || metadata.input.sha256 !== hash || metadata.input.throughMs !== span || (metadata.input.script ?? "raw") !== script ||
      (metadata.input.resumedFrom ?? "") !== resume || models && JSON.stringify(metadata.results.map(x => x.candidate)) !== JSON.stringify(models))
    throw new Error("Saved evidence has different replay settings. No provider called; choose a new run folder.");
  console.log(readFileSync(saved, "utf8"));
  console.log(`Saved live evidence reused from ${folder}. No provider was called; pass --refresh to pay for a new run.`);
  process.exit(0);
}

const child = spawn("dotnet", ["run", "--project", "app-temp/checks/Playback.Checks.csproj", "--no-restore", "-p:UseAppHost=false",
  "-p:OutputPath=bin/verification/net10.0/", "--", live ? "--note-models-live" : "--note-models", folder], {
  windowsHide: true, stdio: ["ignore", "pipe", "pipe"], env: { ...process.env, PLAYBACK_NOTE_MODELS_LIVE: live ? "yes" : "no", PLAYBACK_NOTE_MODELS: modelsArg >= 0 ? process.argv[modelsArg + 1] : "",
    PLAYBACK_NOTE_MODELS_SOURCE: source, PLAYBACK_NOTE_MODELS_MINUTES: minutes, PLAYBACK_NOTE_MODELS_SCRIPT: script,
    PLAYBACK_NOTE_MODELS_MAX_USD: budget || undefined, PLAYBACK_NOTE_MODELS_RESUME: resume || undefined }
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
