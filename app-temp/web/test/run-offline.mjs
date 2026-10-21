// Runs the browser checks that answer every API request from fixture data: starts Vite dev on port 5174, runs each
// check in turn and stops Vite. No API, MongoDB or network. `npm run test:offline` runs them all; name checks to run
// only those, e.g. `npm run test:offline -- note-tree session-loading`.
import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";

const web = fileURLToPath(new URL("..", import.meta.url));
const all = [
  "refactor-baseline",
  "library-sidebar",
  "session-loading",
  "session-rerun",
  "transcript-recording",
  "notes-citations-chat",
  "notes-reading-sources",
  "note-tree",
  "note-instruction",
];
const names = process.argv.length > 2 ? process.argv.slice(2) : all;

process.env.PLAYBACK_OFFLINE_TEST = "yes";
const server = await createServer({
  root: web,
  configLoader: "runner",
  server: { host: "127.0.0.1", port: 5174, strictPort: true },
});
await server.listen();
const failed = [];
try {
  for (const name of names) {
    console.log(`\n== ${name}`);
    // Asynchronous: Vite serves from this process while the check runs.
    const status = await new Promise((resolve) =>
      spawn(process.execPath, [`test/${name}.check.mjs`], {
        cwd: web,
        stdio: "inherit",
      }).on("exit", resolve),
    );
    if (status !== 0) failed.push(name);
  }
} finally {
  await server.close();
}
console.log(
  failed.length
    ? `\nFailed: ${failed.join(", ")}`
    : `\nAll ${names.length} offline browser checks passed`,
);
process.exit(failed.length ? 1 : 0);
