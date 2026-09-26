import { spawnSync } from "node:child_process";

if (!process.env.JEV_API_KEY) {
  console.error("Set JEV_API_KEY in the process environment for the synthetic Jev check");
  process.exit(1);
}

const result = spawnSync("dotnet", [
  "run", "--project", "app-temp/checks/Playback.Checks.csproj", "--no-restore",
  "-p:UseAppHost=false", "-p:OutputPath=bin/verification/net10.0/", "--", "--jev-live",
], {
  stdio: "inherit",
  env: { ...process.env, PLAYBACK_JEV_LIVE_TEST: "yes" },
});
if (result.error) console.error(`Could not run .NET Jev check: ${result.error.message}`);
process.exit(result.status ?? 1);
