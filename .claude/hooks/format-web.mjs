// Claude Code PostToolUse hook: run Prettier on a file Claude just wrote,
// when that file belongs to the web app.
import { execFileSync } from "node:child_process";
import { extname, isAbsolute, relative, resolve } from "node:path";

const formattable = new Set([".ts", ".tsx", ".mjs", ".js", ".css", ".json", ".html"]);
const web = resolve(import.meta.dirname, "../../app-temp/web");

let raw = "";
for await (const chunk of process.stdin) raw += chunk;
const { tool_input, tool_response } = JSON.parse(raw);
const file = resolve(tool_response?.filePath ?? tool_input?.file_path ?? "");

const inside = relative(web, file);
const inWebApp = inside && !inside.startsWith("..") && !isAbsolute(inside);
if (inWebApp && formattable.has(extname(file)) && !inside.includes("node_modules")) {
  const prettier = resolve(web, "node_modules/prettier/bin/prettier.cjs");
  execFileSync(process.execPath, [prettier, "--write", "--log-level", "warn", file], {
    cwd: web,
    stdio: "inherit",
  });
}
