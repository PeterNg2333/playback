// One small paid note call (synthetic text, no audio) that records the real OpenRouter SSE stream and the activity entry.
// Mode: stream (Qwen as the app sends it), models (Alibaba excluded, so the fallback model answers) or fallback
// (OpenRouter unreachable, so Gemini writes). See OpenRouterStreamLiveCheck.cs.
// Run from the repository root: node --env-file-if-exists=.env app-temp/checks/Live/run-openrouter-stream-live.mjs stream
import { spawnSync } from "node:child_process";

const mode = process.argv[2] ?? "stream";
const key = process.env.OPENROUTER_API_KEY;
if (!key || key === "your_openrouter_api_key_here") {
  console.error("Set a real OPENROUTER_API_KEY in the process environment or .env");
  process.exit(1);
}

const result = spawnSync(
  "dotnet",
  ["run", "--project", "app-temp/checks/Playback.Checks.csproj", "--no-restore", "-p:UseAppHost=false",
    "-p:OutputPath=bin/verification/net10.0/", "--", "--openrouter-stream-live", mode],
  { encoding: "utf8", env: { ...process.env, PLAYBACK_OPENROUTER_STREAM_LIVE: "yes" } },
);
for (let text of [result.stdout, result.stderr]) {
  if (!text) continue;
  for (const name of ["OPENROUTER_API_KEY", "GOOGLE_AI_STUDIO_API_KEY"]) if (process.env[name]) text = text.split(process.env[name]).join("[redacted]");
  process.stdout.write(text);
}
if (result.error) {
  console.error(`Could not run the .NET OpenRouter stream check: ${result.error.message}`);
  process.exit(1);
}
process.exit(result.status ?? 1);
