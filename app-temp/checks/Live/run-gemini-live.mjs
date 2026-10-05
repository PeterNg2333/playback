import { spawnSync } from "node:child_process";

const key = process.env.GOOGLE_AI_STUDIO_API_KEY;
if (!key || key === "your_google_ai_studio_api_key_here") {
  console.error("Set a real GOOGLE_AI_STUDIO_API_KEY in the process environment or .env");
  process.exit(1);
}

const result = spawnSync(
  "dotnet",
  [
    "run",
    "--project",
    "app-temp/checks/Playback.Checks.csproj",
    "--no-restore",
    "-p:UseAppHost=false",
    "-p:OutputPath=bin/verification/net10.0/",
    "--",
    "--gemini-live",
  ],
  {
    stdio: "inherit",
    env: { ...process.env, PLAYBACK_GEMINI_LIVE_TEST: "yes" },
  },
);

if (result.error) {
  console.error(`Could not run .NET Gemini check: ${result.error.message}`);
  process.exit(1);
}
process.exit(result.status ?? 1);
