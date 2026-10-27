import { spawnSync } from "node:child_process";

if (!process.env.PLAYBACK_MONGO_URI) {
  console.error("Set PLAYBACK_MONGO_URI in the process environment or .env for the MongoDB storage check");
  process.exit(1);
}

const result = spawnSync("dotnet", [
  "run", "--project", "app-temp/checks/Playback.Checks.csproj", "--no-restore",
  "-p:UseAppHost=false", "-p:OutputPath=bin/verification/net10.0/", "--", "--mongo-storage-live",
], {
  stdio: "inherit",
  // The check is for the PLAYBACK_MONGO_URI database, even when .env says ENVIRONMENT=local.
  env: { ...process.env, ENVIRONMENT: "dev", PLAYBACK_MONGO_STORE_LIVE: "yes" },
});
if (result.error) console.error(`Could not run .NET MongoDB storage check: ${result.error.message}`);
process.exit(result.status ?? 1);
