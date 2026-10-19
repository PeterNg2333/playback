// Two small paid OpenRouter calls (Qwen3.8 Flash, synthetic text, no audio) that record the real SSE stream.
// Run from the repository root: node --env-file-if-exists=.env app-temp/checks/Live/run-openrouter-stream-live.mjs
import { spawnSync } from "node:child_process";

const key = process.env.OPENROUTER_API_KEY;
if (!key || key === "your_openrouter_api_key_here") {
  console.error("Set a real OPENROUTER_API_KEY in the process environment or .env");
  process.exit(1);
}

const result = spawnSync(
  "dotnet",
  ["run", "--project", "app-temp/checks/Playback.Checks.csproj", "--no-restore", "-p:UseAppHost=false",
    "-p:OutputPath=bin/verification/net10.0/", "--", "--openrouter-stream-live"],
  { encoding: "utf8", env: { ...process.env, PLAYBACK_OPENROUTER_STREAM_LIVE: "yes" } },
);
for (const text of [result.stdout, result.stderr]) {
  if (text) process.stdout.write(key ? text.split(key).join("[redacted]") : text);
}
if (result.error) {
  console.error(`Could not run the .NET OpenRouter stream check: ${result.error.message}`);
  process.exit(1);
}
process.exit(result.status ?? 1);
