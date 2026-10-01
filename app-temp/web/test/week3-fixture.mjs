import { readFile } from "node:fs/promises";
import { createHash } from "node:crypto";

export async function week3Fixture(file, seconds = 3600) {
  const text = (await readFile(file, "utf8")).replace(/^\uFEFF/, "");
  let startMs = 0,
    lines = [];
  const passages = [];
  for (const line of text.split(/\r?\n/)) {
    if (/^\d{1,3}:\d{2}(?::\d{2})?$/.test(line.trim())) {
      const endMs =
        line
          .trim()
          .split(":")
          .reduce((value, part) => value * 60 + Number(part), 0) * 1000;
      if (lines.length && startMs < seconds * 1000 && endMs > startMs)
        passages.push({
          startMs,
          endMs: Math.min(endMs, seconds * 1000),
          original: lines.join(" ").trim(),
        });
      startMs = endMs;
      lines = [];
    } else if (line.trim()) lines.push(line.trim());
  }
  if (lines.length && startMs < seconds * 1000)
    passages.push({
      startMs,
      endMs: seconds * 1000,
      original: lines.join(" "),
    });
  if (!passages.length) throw Error("Week 3 transcript has no timed passages");
  return {
    path: file,
    sha256: createHash("sha256").update(text).digest("hex"),
    seconds,
    passages,
  };
}
