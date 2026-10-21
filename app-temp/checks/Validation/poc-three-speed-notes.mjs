// Proof of concept for three-speed notes on the Week 3 captions, the same 164.6 minutes the 2026-10-20 Gemini baseline
// (tree-notes-v15, 30-second batches + organizer) wrote, so the difference from that run is the architecture:
//   1. live: a few jotted points per minute of speech (what the page shows while recording);
//   2. segment: every five minutes, that window written as study notes with block citations and the concepts it defines;
//   3. final: one outline pass places every block in a hierarchy, then one polish pass per top-level topic merges repeats.
// Course context (subject + glossary from Tutorial.txt) is a fixed prefix of every call. The writer is poc-notes-core.mjs.
// `--model` uses the app's PLAYBACK_NOTE_MODEL syntax: vertex:gemini-3.5-flash-lite (default, the baseline's model) or
// openrouter:qwen/qwen3.8-flash@none (the app's default writer), openrouter:qwen/qwen3.7-flash@none, …
// Offline by default: saved replies are reused and the run stops at the first call without one. `--live` calls the
// provider within `--max-usd` (default 1.5). Output: app-temp/data/validation/runs/2026-10-21-poc/notes[-<model>]/.
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import path from "node:path";
import { Ledger, OfflineMiss, clock, courseContext, llm, notesFolder, option, pool, readJson, runFolder, runsRoot, week3Lines, writeJson } from "./poc-lib.mjs";
import { depth, jotSchema, keepCitations, noteWriter, style } from "./poc-notes-core.mjs";

const live = process.argv.includes("--live");
const maxUsd = +option("--max-usd", "1.5");
if (!Number.isFinite(maxUsd) || maxUsd <= 0) throw new Error("--max-usd must be positive");
const model = option("--model", "vertex:gemini-3.5-flash-lite");
const folder = runFolder("2026-10-21-poc");
const out = notesFolder(folder, model);
mkdirSync(out, { recursive: true });
const ctx = { folder, live, ledger: new Ledger(maxUsd) };
const throughMs = 9_875_000;
const lines = week3Lines(throughMs);
const writer = noteWriter(ctx, model, lines);
const { byAlias, row, cite } = writer;

try {
  const course = await courseContext(ctx, model, out);
  const prefix = JSON.stringify({ course });

  // 1. Live: every minute, 1–4 points from that minute, with the three minutes before it as context.
  const minutes = [...Map.groupBy(lines, x => Math.floor(x.startMs / 60_000)).entries()];
  const jots = await pool(minutes, 4, async ([minute, group]) => {
    const start = minute * 60_000;
    const recent = lines.filter(x => x.startMs >= start - 180_000 && x.startMs < start);
    const allowed = new Set(group.map(x => x.alias));
    let reply;
    // The live layer is best effort, as on the page: a failed minute shows nothing and the segment pass still writes it.
    try {
      reply = await llm(ctx, { model, stage: "live", label: `live ${clock(start)}`, schema: jotSchema, maxOutputTokens: 2048,
        system: `You write live lecture jottings: 1–4 short points (one sentence each) on what was said in "new", the last ` +
          `minute of the lecture. "recent" is the speech before it, for context only; do not repeat it. Any explanation, ` +
          `instruction, demonstration step or course arrangement in "new" deserves a point. Return no points only when "new" ` +
          `is nothing but greetings, filler or silence.\n` + style,
        text: prefix + "\n" + JSON.stringify({ recent: recent.map(row), new: group.map(row) }) });
    } catch (error) {
      if (error instanceof OfflineMiss) throw error;
      return { start, points: [], invalid: 0, error: error.message };
    }
    const stats = { invalid: 0 };
    return { start, points: reply.points.map(p => ({ text: keepCitations(p.text, allowed, stats), sources: p.sources.filter(x => allowed.has(x)) })), invalid: stats.invalid };
  });

  // 2. Segment: every five minutes, the window as study notes, continuing the previous window when the subject continues.
  const segments = await writer.segments(prefix);
  writeJson(path.join(out, "segments.json"), segments);
  const blocks = writer.blocksOf(segments);

  // 3. Final: outline, then polish per top-level topic.
  const outline = await writer.outline(prefix, blocks, segments);
  const { topics } = outline;
  writeJson(path.join(out, "outline.json"), outline);
  const { polished, stats: polishStats } = await writer.polish(prefix, topics, blocks);
  writeJson(path.join(out, "polished.json"), Object.fromEntries(polished));

  const concepts = writer.conceptGraph(segments, blocks);
  writeJson(path.join(out, "concept-graph.json"), concepts);
  const notes = writer.render("# Week 3 筆記（POC：三速筆記）", topics, polished, outline, concepts, segments, blocks);
  writeFileSync(path.join(out, "notes.md"), notes);
  writeFileSync(path.join(out, "live-jots.md"), jots.map(j => `**${clock(j.start)}**\n` + j.points.map(p => `- ${cite(p.text)}`).join("\n")).join("\n\n"));
  writeJson(path.join(out, "calls.json"), ctx.ledger.calls);
  const finalBodies = topics.map(t => writer.topicBody(t, polished, blocks));
  const report = compare(notes, finalBodies, topics, jots, segments, polishStats, concepts);
  writeFileSync(path.join(out, "report.md"), report);
  console.log(report);
  console.log(`\nNotes: ${path.join(out, "notes.md")}`);
} catch (error) {
  writeJson(path.join(out, "calls.json"), ctx.ledger.calls);
  if (error instanceof OfflineMiss) { console.log(error.message); process.exitCode = 2; }
  else { console.error(error.message); process.exitCode = 1; }
  console.log(`Spent this run: US$${ctx.ledger.spent.toFixed(4)}`);
}

// The same measures for the baseline's saved notes and this run.
function measure(markdown, citedMinutes) {
  const body = markdown.split("## 溫習問題")[0];
  const narration = (body.match(/講師|講者|老師|教授|lecturer|speaker|instructor/gi) ?? []).length;
  return { characters: body.length, narrationPer1k: +(narration / body.length * 1000).toFixed(2), narration,
    uncertain: (body.match(/\(\?\)|（\?）/g) ?? []).length, citedMinutes };
}

function compare(notes, finalBodies, topics, jots, segments, polishStats, concepts) {
  const baseFolder = path.join(runsRoot, "2026-10-20-wave1-p2-week3-gemini");
  const base = readJson(path.join(baseFolder, "live-results.json")).results[0];
  const baseNote = readJson(path.join(baseFolder, "vertex_gemini-3.5-flash-lite_batch_organize", "note.json"));
  const baseSources = new Map(readJson(path.join(baseFolder, "vertex_gemini-3.5-flash-lite_batch_organize", "sources.json")).map(x => [x.id, x.startMs]));
  const baseMarkdown = readFileSync(path.join(baseFolder, "vertex_gemini-3.5-flash-lite_batch_organize", "notes.md"), "utf8");
  const baseMinutes = new Set(baseNote.citations.flatMap(c => c.sourceIds).map(id => Math.floor((baseSources.get(id) ?? -60_000) / 60_000)).filter(x => x >= 0)).size;
  const baseDepth = Math.max(...baseNote.sections.map(s => { let d = 1; for (let p = s.parentId; p; p = baseNote.sections.find(x => x.id === p)?.parentId) d++; return d; }));
  const pocMinutes = new Set(finalBodies.flatMap(writer.citedAliases).map(x => Math.floor(byAlias.get(x).startMs / 60_000))).size;
  const a = measure(baseMarkdown, baseMinutes), b = measure(notes, pocMinutes);
  const stages = ["course", "live", "segment", "final"].map(s => ctx.ledger.stage(s));
  const total = stages.reduce((n, s) => ({ calls: n.calls + s.calls, inputTokens: n.inputTokens + s.inputTokens, outputTokens: n.outputTokens + s.outputTokens, costUsd: n.costUsd + s.costUsd }),
    { calls: 0, inputTokens: 0, outputTokens: 0, costUsd: 0 });
  const totalMinutes = Math.ceil(throughMs / 60_000);
  const usd = x => "$" + x.toFixed(3);
  return [
    `# Three-speed notes POC — Week 3, 164.6 minutes, zh-Hant, ${model}`, "",
    "Baseline: 2026-10-20-wave1-p2-week3-gemini (tree-notes-v15, 30-second batches + organizer, gemini-3.5-flash-lite), same captions. " +
      "The Qwen3.8 Flash run of that pipeline was cancelled at 133 minutes after 227 writer calls and US$0.63 (interrupted-run.json).", "",
    "| | Baseline | Three-speed POC |", "| --- | --- | --- |",
    `| Provider calls | ${base.calls + base.organizerCalls} (writer ${base.calls}, organizer ${base.organizerCalls}) |${total.calls} (${stages.map(s => `${s.stage} ${s.calls}`).join(", ")}) |`,
    `| Input / output tokens | ${base.inputTokens.toLocaleString()} / ${base.outputTokens.toLocaleString()} | ${total.inputTokens.toLocaleString()} / ${total.outputTokens.toLocaleString()} |`,
    `| Cost (list price × reported tokens) | ${usd(base.costUsd)} | ${usd(total.costUsd)} |`,
    `| Topics (top level), deepest level | ${base.topics} (${baseNote.sections.filter(s => !s.parentId).length}), ${baseDepth} | ${topics.length} (${topics.filter(t => !t.parent).length}), ${Math.max(...topics.map(t => depth(t, topics)))} |`,
    `| Note characters (before appendices) | ${a.characters.toLocaleString()} | ${b.characters.toLocaleString()} |`,
    `| Narrating the class (講師/lecturer… per 1k chars) | ${a.narrationPer1k} (${a.narration}) | ${b.narrationPer1k} (${b.narration}) |`,
    `| Marked reconstructions "(?)" | ${a.uncertain} | ${b.uncertain} |`,
    `| Lecture minutes with a citation | ${a.citedMinutes} / ${totalMinutes} | ${b.citedMinutes} / ${totalMinutes} |`,
    `| Median / max call latency | ${base.medianLatencyMs} / ${base.maxLatencyMs} ms | live ${stages[1].medianLatencyMs} / ${stages[1].maxLatencyMs} ms; segment ${stages[2].medianLatencyMs} / ${stages[2].maxLatencyMs} ms |`,
    "", "## Stages", "", "| Stage | Calls (failed) | Input tokens | Output tokens | Cost | Median latency |", "| --- | --- | --- | --- | --- | --- |",
    ...stages.map(s => `| ${s.stage} | ${s.calls} (${s.failed}) | ${s.inputTokens.toLocaleString()} | ${s.outputTokens.toLocaleString()} | ${usd(s.costUsd)} | ${s.medianLatencyMs} ms |`),
    "", `Live points: ${jots.reduce((n, j) => n + j.points.length, 0)} over ${jots.length} minutes (${jots.filter(j => j.points.length === 0).length} empty, ` +
      `${jots.filter(j => j.error).length} failed); segments: ${segments.length} ` +
      `(${segments.filter(s => s.continuesPrevious).length} continue the previous); blocks: ${segments.reduce((n, s) => n + s.blocks.length, 0)}; ` +
      `invalid citations removed: ${jots.reduce((n, j) => n + j.invalid, 0) + segments.reduce((n, s) => n + s.invalidCitations, 0)}; ` +
      `concepts: ${concepts.nodes.length}, concept links: ${concepts.edges.length}; recognition corrections: ${segments.reduce((n, s) => n + s.corrections.length, 0)}.`,
    `Polish: ${polishStats.filter(x => x.kept === "polished").length} topics polished, ${polishStats.filter(x => x.kept === "original").length} kept original` +
      (polishStats.some(x => x.kept === "original") ? ` (${polishStats.filter(x => x.kept === "original").map(x => x.topic + (x.reason ? ": " + x.reason : ` sources ${x.sourcesKept}, length ${x.lengthRatio}`)).join("; ")})` : "") + ".",
    `Cached replies reused: ${ctx.ledger.calls.filter(x => x.cached).length}; spent this run: ${usd(ctx.ledger.spent)}.`
  ].join("\n");
}
