import { spawn, spawnSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const app = path.join(root, "app-temp");
const children = [];
let stopping = false;

function setupIfMissing(label, exists, command, args) {
  if (exists) return;
  console.log(`Installing ${label} dependencies...`);
  const result = spawnSync(command, args, {
    cwd: root,
    stdio: "inherit",
    shell: process.platform === "win32" && command.endsWith(".cmd"),
  });
  if (result.error || result.status !== 0) {
    throw new Error(
      `${label} setup failed. Check the output above, then run pnpm.cmd dev again.`,
    );
  }
}

export function restoredPackagesAvailable(assets, cache, exists = existsSync) {
  const folders = Object.keys(assets.packageFolders ?? {});
  const files = cache.expectedPackageFiles ?? [];
  const assemblies = Object.values(assets.targets ?? {}).flatMap((target) =>
    Object.entries(target).flatMap(([name, entry]) => {
      const packagePath = assets.libraries?.[name]?.path;
      if (!packagePath) return [];
      return [
        ...Object.keys(entry.compile ?? {}),
        ...Object.keys(entry.runtime ?? {}),
      ]
        .filter((file) => file !== "_._")
        .map((file) => path.join(packagePath, file));
    }),
  );
  return (
    cache.success === true &&
    folders.length > 0 &&
    files.length > 0 &&
    folders.every(exists) &&
    files.every(exists) &&
    assemblies.every((assembly) =>
      folders.some((folder) => exists(path.join(folder, assembly))),
    )
  );
}

function dotnetAssetsReady() {
  try {
    const assets = JSON.parse(
      readFileSync(path.join(app, "api", "obj", "project.assets.json"), "utf8"),
    );
    const cache = JSON.parse(
      readFileSync(path.join(app, "api", "obj", "project.nuget.cache"), "utf8"),
    );
    return restoredPackagesAvailable(assets, cache);
  } catch {
    return false;
  }
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
    await fetch(url, { signal: AbortSignal.timeout(5000) });
    return true;
  } catch {
    return false;
  }
}

async function ready(url, label, child) {
  let consecutive = 0;
  for (let attempt = 0; attempt < 120 && !stopping; attempt++) {
    if (child.exitCode !== null)
      throw new Error(`${label} exited during startup.`);
    try {
      const response = await fetch(url, { signal: AbortSignal.timeout(5000) });
      consecutive = response.ok ? consecutive + 1 : 0;
      if (consecutive === 2) return;
    } catch {
      consecutive = 0;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${label} did not become ready at ${url}`);
}

function startMongo() {
  const result = spawnSync(
    "docker",
    [
      "compose",
      "-f",
      "app-temp/compose.yaml",
      "up",
      "-d",
      "--no-recreate",
      "--pull",
      "never",
      "mongo",
    ],
    {
      cwd: root,
      stdio: "inherit",
    },
  );
  if (result.error || result.status !== 0)
    throw new Error("Docker Compose could not start MongoDB");
}

async function mongoReady() {
  const response = await fetch("http://127.0.0.1:5078/api/health", {
    signal: AbortSignal.timeout(5000),
  });
  return response.ok && (await response.json()).mongo;
}

export async function ensureMongo(
  health,
  {
    start = startMongo,
    check = mongoReady,
    pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms)),
  } = {},
) {
  if (health.mongo) return true;
  if (process.env.PLAYBACK_MONGO_URI) {
    console.warn(
      "Configured MongoDB is unavailable. Check PLAYBACK_MONGO_URI; the bundled container was not started.",
    );
    return false;
  }
  console.warn(
    "MongoDB is unavailable. Starting the Playback MongoDB container...",
  );
  try {
    start();
  } catch (error) {
    console.warn(
      `${error.message}. Open Docker Desktop, then run pnpm.cmd db:up. The UI will start without sessions.`,
    );
    return false;
  }
  for (let attempt = 0; attempt < 15; attempt++) {
    try {
      if (await check()) {
        console.log("MongoDB is ready.");
        return true;
      }
    } catch {
      /* Health checks can fail while MongoDB is starting. */
    }
    await pause(1000);
  }
  console.warn(
    "MongoDB did not become ready. Check Docker Desktop or run pnpm.cmd db:up. The UI will start without sessions.",
  );
  return false;
}

async function main() {
  process.on("SIGINT", () => stop());
  process.on("SIGTERM", () => stop());
  try {
    const apiRunning = await reachable("http://127.0.0.1:5078/api/health");
    const webRunning = await reachable("http://127.0.0.1:5173/");
    if (apiRunning && webRunning) {
      const captureRunning = await fetch(
        "http://127.0.0.1:5078/api/capture/status",
        {
          signal: AbortSignal.timeout(1000),
        },
      )
        .then((response) => response.ok)
        .catch(() => false);
      if (!captureRunning)
        throw new Error(
          "An older Playback API is running. Stop it, then run pnpm.cmd dev again.",
        );
      const health = await (
        await fetch("http://127.0.0.1:5078/api/health")
      ).json();
      if (!health.automaticAsr)
        throw new Error(
          "An older Playback API is running. Stop it with Ctrl+C, then run pnpm.cmd dev again.",
        );
      await ensureMongo(health);
      console.log(
        "Playback is already running at http://127.0.0.1:5173/. Open it in your browser.",
      );
      process.exit(0);
    }
    if (apiRunning || webRunning)
      throw new Error(
        `${apiRunning ? "API on 5078" : "Web on 5173"} is already running. Stop it, then run pnpm.cmd dev again.`,
      );
    setupIfMissing(
      "Web",
      existsSync(path.join(app, "web", "node_modules")),
      process.platform === "win32" ? "npm.cmd" : "npm",
      ["--prefix", "app-temp/web", "ci"],
    );
    setupIfMissing(".NET", dotnetAssetsReady(), "dotnet", [
      "restore",
      "app-temp/api/Playback.Api.csproj",
    ]);
    const api = run("API", "dotnet", [
      "run",
      "--no-restore",
      "--project",
      "app-temp/api/Playback.Api.csproj",
    ]);
    await ready("http://127.0.0.1:5078/api/health", "API", api);
    const health = await (
      await fetch("http://127.0.0.1:5078/api/health")
    ).json();
    await ensureMongo(health);

    const npm = process.platform === "win32" ? "npm.cmd" : "npm";
    const web = run("Web", npm, ["--prefix", "app-temp/web", "run", "dev"]);
    await ready("http://127.0.0.1:5173/", "Web", web);

    console.log(
      "Playback is running at http://127.0.0.1:5173/. Press Ctrl+C to stop API and Web.",
    );
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    stop(1);
  }
}

if (
  process.argv[1] &&
  path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)
)
  main();
