import { spawnSync } from "node:child_process";

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
    "--asr-synthetic-live",
  ],
  {
    stdio: "inherit",
    env: { ...process.env, PLAYBACK_ASR_LIVE_CHECK: "yes" },
  },
);

if (result.error) {
  console.error(`Could not run .NET ASR check: ${result.error.message}`);
  process.exit(1);
}
process.exit(result.status ?? 1);
