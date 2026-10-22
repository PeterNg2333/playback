// Live acceptance of three-speed notes on the whole Week 3 lecture: the product's NoteWorkflow, local MongoDB
// (playback_e2e) and the real note writer (Qwen3.8 Flash on OpenRouter, then GLM, then Gemini). Text only: no audio is
// read or uploaded. Replaces the placement-era week3-e2e check.
//
// Default: offline. Prints the saved results.json of the run folder; no network, no database, no build.
//   --live [--max-usd 0.3]   seed, record (jot each minute, segment each five minutes, then Stop: outline, polish,
//                            summary), edit (one corrected line, notes-only rerun), rebuild (one locked block), report.
//                            Every phase with a saved <phase>.json is reused, never paid twice; the cap covers the
//                            folder's whole ledger.jsonl, earlier attempts included.
//   --finish [--max-usd 0.05] only Stop's outline and polish again with the current code, on a copy of the record phase's
//                            notes before its outline; results and its own ledger in <folder>/finish/. Paid unless --dry.
//   --dry                    the same phases with the fixture note model: needs MongoDB, no network, no cost.
//   --browser                the saved session in the real page: offline API (5079) on playback_e2e and Vite (5176).
//   --folder <path>          run folder (default app-temp/data/validation/runs/2026-10-22-three-speed-live).
// Needs a build: dotnet build app-temp/checks/Playback.Checks.csproj --no-restore -p:UseAppHost=false
//   -p:OutputPath=bin/verification/net10.0/   and, for --live, --dry and --browser, pnpm.cmd db:up.
import { spawn, spawnSync } from "node:child_process";
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
// pnpm passes its "--" separator through.
const args = process.argv.slice(2).filter((arg) => arg !== "--");
const option = (name, fallback) => (args.includes(name) ? args[args.indexOf(name) + 1] : fallback);
const known = ["--live", "--dry", "--browser", "--finish", "--max-usd", "--folder"];
for (const [i, arg] of args.entries())
  if (!known.includes(arg) && !["--max-usd", "--folder"].includes(args[i - 1])) throw new Error(`Unknown option ${arg}`);
const live = args.includes("--live");
const dry = args.includes("--dry");
const finish = args.includes("--finish");
const folder = path.resolve(root, option("--folder", "app-temp/data/validation/runs/2026-10-22-three-speed-live"));
const maxUsd = Number(option("--max-usd", finish ? "0.05" : "0.3"));
if (!(maxUsd > 0 && maxUsd <= 1)) throw new Error("--max-usd must be above 0 and at most 1");
const dll = path.join(root, "app-temp/checks/bin/verification/net10.0/Playback.Checks.dll");
const secrets = ["GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "OPENROUTER_API_KEY"];
const redact = (text) => secrets.reduce((safe, name) => (process.env[name] ? safe.split(process.env[name]).join("[redacted]") : safe), text);

if (!live && !dry && !finish && !args.includes("--browser")) {
  const file = path.join(folder, "results.json");
  if (!existsSync(file)) {
    console.log("Offline default: no saved results in " + path.relative(root, folder) + ". --live runs the paid acceptance (see the header).");
    process.exit(0);
  }
  const r = JSON.parse(readFileSync(file, "utf8"));
  const browser = existsSync(path.join(folder, "browser.json")) ? JSON.parse(readFileSync(path.join(folder, "browser.json"), "utf8")) : null;
  console.log(
    `Saved three-speed live run (${r.generatedAt}): ${r.totalCalls} calls, US$${r.totalCostUsd}; ` +
      `after Stop ${r.afterStop.topics} topics (${r.afterStop.topLevel} top level, depth ${r.afterStop.depth}), ` +
      `${r.afterStop.citedMinutes}/${r.afterStop.lectureMinutes} minutes cited; Stop to outline ${(r.stop.outlineDoneMs / 1000).toFixed(0)} s; ` +
      `edit ${r.edit?.passed ? "passed" : "failed"}, rebuild ${r.rebuild?.passed ? "passed" : "failed"}, browser ${browser ? (browser.passed ? "passed" : "failed") : "not run"}.`,
  );
  const again = path.join(folder, "finish", "finish.json");
  const f = existsSync(again) ? JSON.parse(readFileSync(again, "utf8")) : null;
  if (f)
    console.log(
      `Finish again: ${f.passed ? "passed" : "failed"}; ${f.polish.calls} polish calls, longest ${(f.polish.maxMs / 1000).toFixed(0)} s, ` +
        `${f.polish.deadlineFallbacks} past the deadline, ${f.unpolishedTopics.length} of ${f.topics} topics unpolished; US$${f.costUsd}.`,
    );
  process.exit(r.passed && browser?.passed !== false && f?.passed !== false ? 0 : 1);
}

mkdirSync(folder, { recursive: true });

function phase(name, env = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn("dotnet", [dll, "--three-speed-live", folder, name], {
      cwd: root, windowsHide: true, stdio: ["ignore", "pipe", "pipe"],
      env: { ...process.env, ...env },
    });
    for (const stream of [child.stdout, child.stderr])
      stream.on("data", (bytes) => {
        const safe = redact(bytes.toString());
        process.stdout.write(safe);
        appendFileSync(path.join(folder, `${name}.log`), safe);
      });
    child.on("error", reject);
    child.on("close", (code) => (code === 0 ? resolve() : reject(new Error(`Phase ${name} failed (${code})`))));
  });
}

if (finish) {
  if (!existsSync(dll)) throw new Error("Build the checks to bin/verification first (see the header)");
  if (!dry && !process.env.OPENROUTER_API_KEY) throw new Error("OPENROUTER_API_KEY must be in the process environment");
  await phase("finish", dry ? { PLAYBACK_THREE_SPEED_DRY: "yes" } : { PLAYBACK_THREE_SPEED_LIVE: "yes", PLAYBACK_THREE_SPEED_MAX_USD: String(maxUsd) });
  process.exit(0);
}

if (live || dry) {
  if (!existsSync(dll)) throw new Error("Build the checks to bin/verification first (see the header)");
  if (live && !dry && !process.env.OPENROUTER_API_KEY) throw new Error("OPENROUTER_API_KEY must be in the process environment");
  const env = dry
    ? { PLAYBACK_THREE_SPEED_DRY: "yes" }
    : { PLAYBACK_THREE_SPEED_LIVE: "yes", PLAYBACK_THREE_SPEED_MAX_USD: String(maxUsd) };
  for (const name of ["seed", "record", "edit", "rebuild", "report"]) await phase(name, env);
  if (!args.includes("--browser")) process.exit(0);
}

// ── Browser: the saved session in the page, against an offline API on playback_e2e (no provider can be called) ──

const seed = JSON.parse(readFileSync(path.join(folder, "seed.json"), "utf8"));
const results = JSON.parse(readFileSync(path.join(folder, "results.json"), "utf8"));
const api = "http://127.0.0.1:5079", web = "http://127.0.0.1:5176";
for (const url of [api + "/api/health", web])
  if (await fetch(url, { signal: AbortSignal.timeout(1000) }).catch(() => null)) throw new Error(`${url} is already in use`);
const apiDll = path.join(root, "app-temp/api/bin/verification/net10.0/Playback.Api.dll");
if (!existsSync(apiDll)) throw new Error("Build the API to bin/verification first");
const children = [];
const stop = () => { for (const child of children) if (child.exitCode === null) spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" }); };
process.once("SIGINT", () => { stop(); process.exit(130); });
const start = (command, argv, env, cwd = root) => {
  const child = spawn(command, argv, { cwd, env: { ...process.env, ...env }, windowsHide: true, stdio: ["ignore", "pipe", "pipe"], shell: command.endsWith(".cmd") });
  for (const stream of [child.stdout, child.stderr]) stream.on("data", (bytes) => appendFileSync(path.join(folder, "browser-servers.log"), redact(bytes.toString())));
  children.push(child);
  return child;
};
async function ready(url, child, label) {
  for (let i = 0; i < 90; i++) {
    if (child.exitCode !== null) throw new Error(`${label} exited during startup`);
    if ((await fetch(url, { signal: AbortSignal.timeout(2000) }).catch(() => null))?.ok) return;
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(`${label} did not become ready`);
}
const offline = {
  ASPNETCORE_ENVIRONMENT: "Development", PLAYBACK_OFFLINE_TEST: "yes", PLAYBACK_MONGO_URI: "mongodb://127.0.0.1:27017",
  PLAYBACK_MONGO_DATABASE: "playback_e2e", PLAYBACK_AUTO_NOTES: "no", PLAYBACK_AUTO_TERMS: "no", PLAYBACK_AUTO_TRANSLATION: "no",
  PLAYBACK_ASR_REVIEW: "no", PLAYBACK_RESUME_SAVED_ASR: "no",
};
const report = { testedAt: new Date().toISOString(), sessionId: seed.id, passed: false, checks: {}, errors: [] };
try {
  const apiProcess = start("dotnet", [apiDll], offline);
  await ready(api + "/api/health", apiProcess, "API");
  const vite = start(process.platform === "win32" ? "npx.cmd" : "npx", ["vite", "--host", "127.0.0.1", "--port", "5176", "--strictPort", "--configLoader", "runner"],
    { PLAYBACK_OFFLINE_TEST: "yes" }, path.join(root, "app-temp/web"));
  await ready(web, vite, "Vite");
  const { checkSession } = await import("../../web/test/three-speed-live.check.mjs");
  report.checks = await checkSession({ web, folder, session: seed, results });
  report.passed = true;
} catch (error) {
  report.errors.push(redact(String(error?.stack ?? error)));
} finally {
  stop();
  writeFileSync(path.join(folder, "browser.json"), JSON.stringify(report, null, 2));
}
console.log(report.passed ? `Browser: passed (${JSON.stringify(report.checks)})` : `Browser: failed\n${report.errors.join("\n")}`);
process.exit(report.passed ? 0 : 1);
