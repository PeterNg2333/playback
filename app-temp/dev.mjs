import { spawn, spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const app = path.join(root, "app-temp");
const children = [];
let stopping = false;

if (!existsSync(path.join(app, "web", "node_modules"))) {
  console.error("Missing web dependencies. Run pnpm.cmd setup first.");
  process.exit(1);
}
if (!existsSync(path.join(app, "api", "obj", "project.assets.json"))) {
  console.error("Missing .NET dependencies. Run pnpm.cmd setup first.");
  process.exit(1);
}

function stop(exitCode = 0) {
  if (stopping) return;
  stopping = true;
  for (const child of children) {
    if (child.exitCode !== null || !child.pid) continue;
    if (process.platform === "win32") {
      spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], {
        stdio: "ignore",
      });
    } else {
      child.kill("SIGTERM");
    }
  }
  process.exit(exitCode);
}

function run(label, command, args, options = {}) {
  const child = spawn(command, args, {
    cwd: root,
    stdio: "inherit",
    shell: process.platform === "win32" && command.endsWith(".cmd"),
    ...options,
  });
  children.push(child);
  child.on("error", (error) => {
    console.error(`${label}: ${error.message}`);
    stop(1);
  });
  child.on("exit", (code) => {
    if (stopping) return;
    console.error(`${label} exited (${code ?? "unknown"}).`);
    stop(code ?? 1);
  });
  return child;
}

async function reachable(url) {
  try {
    await fetch(url, { signal: AbortSignal.timeout(1000) });
    return true;
  } catch {
    return false;
  }
}

async function ready(url, label, child) {
  let consecutive = 0;
  for (let attempt = 0; attempt < 120 && !stopping; attempt++) {
    if (child.exitCode !== null) throw new Error(`${label} exited during startup.`);
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(1000) });
      consecutive = response.ok ? consecutive + 1 : 0;
      if (consecutive === 4) return;
    } catch {
      consecutive = 0;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${label} did not become ready at ${url}`);
}

process.on("SIGINT", () => stop());
process.on("SIGTERM", () => stop());

try {
  const apiRunning = await reachable("http://127.0.0.1:5078/api/health");
  const webRunning = await reachable("http://127.0.0.1:5173/");
  if (apiRunning && webRunning) {
    const captureRunning = await fetch("http://127.0.0.1:5078/api/capture/status", {
      signal: AbortSignal.timeout(1000),
    }).then((response) => response.ok).catch(() => false);
    if (!captureRunning) throw new Error("An older Playback API is running. Stop it, then run pnpm.cmd dev again.");
    const health = await (await fetch("http://127.0.0.1:5078/api/health")).json();
    if (!health.automaticAsr) throw new Error("An older Playback API is running. Stop it with Ctrl+C, then run pnpm.cmd dev again.");
    console.log("Playback is already running at http://127.0.0.1:5173/. Open it in your browser.");
    process.exit(0);
  }
  if (apiRunning || webRunning)
    throw new Error(`${apiRunning ? "API on 5078" : "Web on 5173"} is already running. Stop it, then run pnpm.cmd dev again.`);
  const api = run("API", "dotnet", ["run", "--no-restore", "--project", "app-temp/api/Playback.Api.csproj"]);
  await ready("http://127.0.0.1:5078/api/health", "API", api);
  const health = await (await fetch("http://127.0.0.1:5078/api/health")).json();
  if (!health.mongo) {
    throw new Error("MongoDB is unavailable. Start Docker Desktop, then run pnpm.cmd db:up.");
  }

  const npm = process.platform === "win32" ? "npm.cmd" : "npm";
  const web = run("Web", npm, ["--prefix", "app-temp/web", "run", "dev"]);
  await ready("http://127.0.0.1:5173/", "Web", web);

  console.log("Playback is running at http://127.0.0.1:5173/. Press Ctrl+C to stop API and Web.");
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  stop(1);
}
