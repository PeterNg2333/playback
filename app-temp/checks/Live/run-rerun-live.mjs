// Paid live test of Session rerun and the concept graph on the Week 3 sample. Explicit opt-in only.
// Starts an isolated API on 5082 (database playback_e2e, its own data folder) and, with --graph, the LightRAG sidecar on 5091.
// The developer API on 5078 is never stopped. Retains the test session and writes a report under output/rerun-live/.
import { spawn, spawnSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const app = path.join(root, "app-temp");
const options = new Set(process.argv.slice(2));
if (!options.has("--allow-paid-live")) throw new Error("Pass --allow-paid-live after approving paid ASR/Gemini calls and audio upload");
if ([...options].some((x) => !["--allow-paid-live", "--graph"].includes(x))) throw new Error("Unknown option");
const graph = options.has("--graph");
if (!process.env.GOOGLE_AI_STUDIO_API_KEY || !process.env.OPENROUTER_API_KEY)
  throw new Error("Gemini and OpenRouter credentials must be in the process environment");
if (process.env.PLAYBACK_MONGO_URI && !/^mongodb:\/\/(?:127\.0\.0\.1|localhost):\d+\/?$/.test(process.env.PLAYBACK_MONGO_URI))
  throw new Error("The live test uses the localhost MongoDB only");

const dll = (name) => path.join(app, name, "bin", "verification", "net10.0", `Playback.${name === "api" ? "Api" : "Checks"}.dll`);
for (const name of ["api", "checks"]) if (!existsSync(dll(name))) throw new Error(`Build ${name} to bin/verification first`);
const python = path.join(app, "graph", ".venv", "Scripts", "python.exe");
if (graph && !existsSync(python)) throw new Error("Install the concept graph sidecar first (app-temp/graph/README.zh-HK.md)");
const run = path.join(root, "output", "rerun-live", new Date().toISOString().replace(/[:.]/g, "-"));
mkdirSync(run, { recursive: true });
const children = new Set();
const stop = (child) => {
  if (!child?.pid || child.exitCode !== null) return;
  spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
};
process.once("SIGINT", () => { for (const child of children) stop(child); process.exit(130); });

async function ready(url, child, label) {
  for (let attempt = 0; attempt < 90; attempt++) {
    if (child.exitCode !== null) throw new Error(`${label} exited during startup`);
    const response = await fetch(url, { signal: AbortSignal.timeout(2000) }).catch(() => null);
    if (response?.ok) return;
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
  throw new Error(`${label} did not become ready`);
}
for (const url of ["http://127.0.0.1:5082/healthz", "http://127.0.0.1:5091/health"])
  if (await fetch(url, { signal: AbortSignal.timeout(1000) }).catch(() => null)) throw new Error(`${url} is already in use`);

const base = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "Development",
  PORT: "5082",
  PLAYBACK_MONGO_DATABASE: "playback_e2e",
  Storage__DataPath: path.join(run, "data"),
  Storage__KeyPath: path.join(run, "keys"),
  PLAYBACK_OFFLINE_TEST: "no",
  PLAYBACK_PAUSE_EXTERNAL_ASR: "no",
  PLAYBACK_AUTO_NOTES: "no",
  PLAYBACK_AUTO_TERMS: "no",
  PLAYBACK_AUTO_ORGANIZE: "no",
  PLAYBACK_AUTO_TRANSLATION: "no",
  PLAYBACK_ASR_REVIEW: "no",
  // Other sessions in playback_e2e keep their own audio elsewhere; never resume their pending ASR here.
  PLAYBACK_RESUME_SAVED_ASR: "no",
  PLAYBACK_RERUN_LIVE: "yes",
  PLAYBACK_GRAPH_PORT: "5091",
  Logging__LogLevel__Microsoft: "Warning",
};
delete base.PLAYBACK_DEMO_PASSWORD;
delete base.PLAYBACK_PUBLIC_ORIGIN;
delete base.PLAYBACK_VALIDATION_PORT;
delete base.PLAYBACK_CONCEPT_GRAPH;

function api(env) {
  const child = spawn("dotnet", [dll("api"), "--webroot", path.join(app, "web", "dist")], { cwd: path.join(app, "api"), env, stdio: "inherit", windowsHide: true });
  children.add(child);
  return child;
}
async function check(args, env) {
  const child = spawn("dotnet", [dll("checks"), ...args], { cwd: root, env, stdio: "inherit", windowsHide: true });
  children.add(child);
  const code = await new Promise((resolve) => child.once("exit", resolve));
  if (code !== 0) throw new Error(`Live check ${args[0]} failed (${code}); artifacts in ${run}`);
}

let host, sidecar;
try {
  // 1. Rerun on 8-second chunks, without the concept graph.
  host = api(base);
  await ready("http://127.0.0.1:5082/healthz", host, "Live API");
  await check(["--rerun-live", path.join(app, "data", "test-audio", "sampleAudio"), run], base);
  const session = readFileSync(path.join(run, "session.txt"), "utf8").trim();
  await check(["--rerun-live-ask", session, run, "keyword"], base);
  stop(host);
  if (graph) {
    // 2. The same questions with the concept graph built from the rerun transcript.
    const graphEnv = { ...base, PLAYBACK_CONCEPT_GRAPH: "yes", PLAYBACK_GRAPH_DIR: path.join(run, "data", "graph") };
    sidecar = spawn(python, ["-I", path.join(app, "graph", "server.py")], { cwd: path.join(app, "graph"), env: graphEnv, stdio: "inherit", windowsHide: true });
    children.add(sidecar);
    await ready("http://127.0.0.1:5091/health", sidecar, "Concept graph sidecar");
    host = api(graphEnv);
    await ready("http://127.0.0.1:5082/healthz", host, "Live API with concept graph");
    await check(["--rerun-live-ask", session, run, "graph"], graphEnv);
  }
  console.log(`Rerun live test finished. Report: ${run}`);
} finally {
  stop(host);
  stop(sidecar);
}
