// Proof of concept for recognizing a longer window with course context, on the app's ASR models. Three 90-second windows
// of the Week 3 sample audio, where the captions mishear Windows-internals names, are recognized by:
//   qwen-8s / whisper-8s          Qwen3-ASR 1.7B or Whisper Large V3 on OpenRouter, fixed 8-second pieces (close to
//                                 today's first pass, without VAD sealing);
//   qwen-90s / whisper-90s        the same models on the whole window (today's Rerun session window);
//   qwen-90s+ctx / whisper-90s+ctx the window with the course glossary as a provider prompt (provider.options). OpenRouter
//                                 cannot pin a transcription provider and may drop an unknown field, so the report says
//                                 whether the text changed at all;
//   gemini-90s / gemini-90s+ctx   Gemini 3.5 Flash-Lite audio, an extra reference outside the project's ASR stack.
// There is no human reference transcript: the report counts canonical names, known mishearings and words (a much shorter
// text than the 8-second pieces means speech was dropped), and saves each window as WAV so a person can listen and judge.
// Offline by default (saved replies only); `--live` uploads the windows within `--max-usd` (default 0.3). OpenRouter
// refuses audio while the key has under US$0.50 of its limit left (HTTP 402).
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { Ledger, OfflineMiss, clock, courseContext, gemini, openrouterAsr, option, runFolder, sampleFolder, week3Lines, writeJson } from "./poc-lib.mjs";

const live = process.argv.includes("--live");
const maxUsd = +option("--max-usd", "0.3");
if (!Number.isFinite(maxUsd) || maxUsd <= 0) throw new Error("--max-usd must be positive");
const folder = runFolder("2026-10-21-poc");
const out = path.join(folder, "asr");
mkdirSync(out, { recursive: true });
const ctx = { folder, live, ledger: new Ledger(maxUsd) };
const windows = [
  { id: "w1", startS: 775, seconds: 90, about: "notepad → kernel32 → ntdll, the Nt create-file function, an address in the debugger" },
  { id: "w2", startS: 920, seconds: 90, about: "VM internet access, local kernel debugging, Microsoft symbol server" },
  { id: "w3", startS: 1050, seconds: 90, about: "examining ntdll!* in the debugger, Nt and Zw prefixes at one address, ntoskrnl" }
];
// Names a correct transcript of these windows should contain, and the captions' mishearings of them. Both lists come from
// the captions and Windows-internals knowledge, not from listening; check the WAVs before trusting a count.
const canonical = { ntdll: /ntdll/i, kernel32: /kernel ?32/i, WinDbg: /win ?dbg|windows debugger/i, "symbol server": /symbol server/i,
  ntoskrnl: /ntoskrnl|nt ?os ?kr?nl/i, "Zw prefix": /\bzw/i, "Nt function": /\bNt[A-Z][a-z]+/, lkd: /\blkd\b/i };
const misheard = { needle: /needle/i, Nptel: /nptel/i, "green/Kevin debugger": /green debugger|kevin debugger/i, "sample server": /sample server/i,
  "set w": /set w\b/i, LXD: /\bLXD\b/, "OS and kernel": /os and kernel/i, "mnt query": /mnt query/i, "grave file": /grave file/i };

const verbatim = "Transcribe this lecture audio verbatim, in the language spoken (mostly English, possibly Cantonese). Do not " +
  "summarize, paraphrase, translate or add words; leave out only filler sounds such as um and uh. Write technical names, " +
  "commands and code the way they are normally written. If a word is unclear, write what you hear followed by (?). " +
  "Output only the transcript text.";

function cut(file, startS, seconds) {
  if (!existsSync(file)) execFileSync("ffmpeg", ["-v", "error", "-y", "-ss", String(startS), "-t", String(seconds), "-i",
    path.join(sampleFolder, "sampleAudio.m4a"), "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", file], { windowsHide: true });
  return readFileSync(file);
}

const words = text => (text ?? "").split(/\s+/).filter(Boolean).length;
const score = text => ({
  canonical: Object.entries(canonical).filter(([, re]) => re.test(text)).map(([k]) => k),
  misheard: Object.entries(misheard).filter(([, re]) => re.test(text)).map(([k]) => k)
});

try {
  // The glossary built by the earlier run (Gemini, from Tutorial.txt) is reused so every system gets the same context.
  const course = await courseContext(ctx);
  const glossary = course.terms.map(t => t.term + (t.heardAs.length ? ` (misheard as: ${t.heardAs.join(", ")})` : "")).join("; ");
  // Whisper reads a prompt as earlier transcript (at most 224 tokens), so it gets the subject and the canonical names only.
  const hint = `Windows internals lecture. Terms: ${course.terms.map(t => t.term).join(", ")}.`;
  const lines = week3Lines(1_200_000);
  const results = [];
  // A system that fails (for example an account limit or an unknown provider field) is reported; the others still run.
  const failures = {};
  const attempt = async (system, run) => {
    if (failures[system]) return null;
    try { return await run(); }
    catch (error) {
      if (error instanceof OfflineMiss) throw error;
      failures[system] = error.message.slice(0, 200);
      return null;
    }
  };
  const pieces = (w, system, model) => attempt(system, async () => {
    const texts = [];
    for (let s = 0; s < w.seconds; s += 8)
      texts.push(await openrouterAsr(ctx, { stage: system, label: `${w.id} ${system} @${s}`, model,
        wav: cut(path.join(out, `${w.id}-${String(s).padStart(2, "0")}.wav`), w.startS + s, Math.min(8, w.seconds - s)) }));
    return texts.join(" ");
  });
  for (const w of windows) {
    const wav = cut(path.join(out, `${w.id}.wav`), w.startS, w.seconds);
    const audio = { inlineData: { mimeType: "audio/wav", data: wav.toString("base64") } };
    const texts = {
      captions: lines.filter(x => x.startMs >= w.startS * 1000 - 5000 && x.startMs < (w.startS + w.seconds) * 1000).map(x => x.text).join(" "),
      "qwen-8s": await pieces(w, "qwen-8s", "qwen/qwen3-asr-1.7b"),
      "qwen-90s": await attempt("qwen-90s", () => openrouterAsr(ctx, { stage: "qwen-90s", label: `${w.id} qwen 90s`, wav })),
      "qwen-90s+ctx": await attempt("qwen-90s+ctx", () => openrouterAsr(ctx, { stage: "qwen-90s+ctx", label: `${w.id} qwen 90s + context`, wav,
        options: { "deepinfra/us": { prompt: hint, context: hint } } })),
      "whisper-8s": await pieces(w, "whisper-8s", "openai/whisper-large-v3"),
      "whisper-90s": await attempt("whisper-90s", () => openrouterAsr(ctx, { stage: "whisper-90s", label: `${w.id} whisper 90s`, model: "openai/whisper-large-v3", wav })),
      "whisper-90s+ctx": await attempt("whisper-90s+ctx", () => openrouterAsr(ctx, { stage: "whisper-90s+ctx", label: `${w.id} whisper 90s + context`,
        model: "openai/whisper-large-v3", wav, options: { groq: { prompt: hint }, together: { prompt: hint }, "deepinfra/us": { prompt: hint, initial_prompt: hint } } })),
      "gemini-90s": await attempt("gemini-90s", () => gemini(ctx, { stage: "gemini-90s", label: `${w.id} gemini`, system: verbatim, maxOutputTokens: 4096,
        parts: [audio, { text: "Transcribe." }] })),
      "gemini-90s+ctx": await attempt("gemini-90s+ctx", () => gemini(ctx, { stage: "gemini-90s+ctx", label: `${w.id} gemini + context`, maxOutputTokens: 4096,
        system: verbatim + "\nCourse context: " + course.subject + "\nGlossary of terms this course uses, with how speech " +
          "recognition has misheard them: " + glossary + "\nUse a glossary spelling only where the audio says that term; " +
          "never insert a term that was not spoken.",
        parts: [audio, { text: "Transcribe." }] }))
    };
    results.push({ ...w, texts, scores: Object.fromEntries(Object.entries(texts).map(([k, v]) => [k, score(v ?? "")])) });
  }
  writeJson(path.join(out, "results.json"), results);
  writeJson(path.join(out, "calls.json"), ctx.ledger.calls);
  const systems = ["captions", "qwen-8s", "qwen-90s", "qwen-90s+ctx", "whisper-8s", "whisper-90s", "whisper-90s+ctx", "gemini-90s", "gemini-90s+ctx"];
  const stages = systems.slice(1).map(s => ctx.ledger.stage(s));
  const same = (a, b) => results.every(r => r.texts[a] !== null && r.texts[a] === r.texts[b]);
  const report = [
    "# ASR context POC — Week 3 sample, three 90-second windows", "",
    `Course glossary (${course.terms.length} terms, built from Tutorial.txt): ${course.terms.map(t => t.term).join(", ")}`, "",
    "| System | Canonical names found (of " + Object.keys(canonical).length * windows.length + ") | Known mishearings found | Words w1 / w2 / w3 | Calls | Cost | Median latency |",
    "| --- | --- | --- | --- | --- | --- | --- |",
    ...systems.map(s => {
      if (failures[s]) return `| ${s} | failed | — | — | — | — | — |`;
      const stage = stages.find(x => x.stage === s);
      const found = results.reduce((n, r) => n + r.scores[s].canonical.length, 0);
      const wrong = results.reduce((n, r) => n + r.scores[s].misheard.length, 0);
      const reported = ctx.ledger.calls.filter(x => x.stage === s && "costReported" in x).every(x => x.costReported);
      return `| ${s} | ${found} | ${wrong} | ${results.map(r => words(r.texts[s])).join(" / ")} | ${stage?.calls ?? "—"} | ` +
        `${stage ? "$" + stage.costUsd.toFixed(4) + (reported ? "" : " (not all reported)") : "—"} | ${stage ? stage.medianLatencyMs + " ms" : "—"} |`;
    }), "",
    ...[["qwen-90s+ctx", "qwen-90s"], ["whisper-90s+ctx", "whisper-90s"]].filter(([a, b]) => !failures[a] && !failures[b])
      .map(([a, b]) => `${a} ${same(a, b) ? "returned exactly the same text as" : "differs from"} ${b}${same(a, b) ? " (the prompt was probably dropped)" : ""}.`), "",
    ...results.flatMap(r => [`## ${r.id} ${clock(r.startS * 1000)}–${clock((r.startS + r.seconds) * 1000)}: ${r.about}`, "",
      `Audio: ${path.join(out, r.id + ".wav")}`, "",
      ...systems.filter(s => !failures[s]).flatMap(s => [`**${s}** — names: ${r.scores[s].canonical.join(", ") || "none"}; mishearings: ${r.scores[s].misheard.join(", ") || "none"}`, "",
        "> " + r.texts[s].replace(/\s+/g, " ").trim(), ""])]),
    ...Object.entries(failures).map(([s, reason]) => `Failed: ${s}: ${reason}`),
    `Cached replies reused: ${ctx.ledger.calls.filter(x => x.cached).length}; spent this run: $${ctx.ledger.spent.toFixed(4)}.`
  ].join("\n");
  writeFileSync(path.join(out, "report.md"), report);
  console.log(report);
} catch (error) {
  writeJson(path.join(out, "calls.json"), ctx.ledger.calls);
  if (error instanceof OfflineMiss) { console.log(error.message); process.exitCode = 2; }
  else { console.error(error.message); process.exitCode = 1; }
  console.log(`Spent this run: $${ctx.ledger.spent.toFixed(4)}`);
}
