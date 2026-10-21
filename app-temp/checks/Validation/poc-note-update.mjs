// Proof of concept for keeping three-speed notes current through the note graph, starting from a saved notes run
// (default: Qwen3.8 Flash). The graph is lineage (source → block → topic, from citations) plus the concept graph
// (concept ↔ block, from the segment pass).
// Scenario 1, the transcript changes: the Qwen3-ASR 90-second rerun of three windows (asr/results.json) supersedes the
// caption lines it covers, as Session rerun does.
//   graph:   rewrite only the five-minute segments whose sources changed, put their blocks where the old ones were, check
//            other topics that mention a concept the change removed or added, and polish only the topics that changed;
//   rebuild: write every segment, the outline and every topic again (the app's Rebuild).
// Scenario 2, one topic is regenerated on request, with and without the related blocks the concept graph links to it.
// Offline by default (saved replies only); `--live` calls the provider within `--max-usd` (default 0.3).
// Output: app-temp/data/validation/runs/2026-10-21-poc/update-<model>/.
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { Ledger, OfflineMiss, clock, courseContext, llm, notesFolder, option, pool, readJson, runFolder, week3Lines, writeJson } from "./poc-lib.mjs";
import { citations, keepCitations, noteWriter, style } from "./poc-notes-core.mjs";

const live = process.argv.includes("--live");
const maxUsd = +option("--max-usd", "0.3");
if (!Number.isFinite(maxUsd) || maxUsd <= 0) throw new Error("--max-usd must be positive");
const model = option("--model", "openrouter:qwen/qwen3.8-flash@none");
const folder = runFolder("2026-10-21-poc");
const base = notesFolder(folder, model);
const out = path.join(folder, "update-" + (path.basename(base).replace(/^notes-?/, "") || "gemini"));
mkdirSync(out, { recursive: true });
const ctx = { folder, live, ledger: new Ledger(maxUsd) };
const throughMs = 9_875_000;

// Scenario 1's change: each rerun window becomes one new source (a new alias after the last caption line) that supersedes
// the caption lines starting inside it. Aliases are identities, not positions, so unchanged lines keep theirs.
const lines0 = week3Lines(throughMs);
const asr = readJson(path.join(folder, "asr", "results.json"));
const reruns = asr.map((w, i) => ({ alias: "T" + (lines0.length + i + 1), startMs: w.startS * 1000, endMs: (w.startS + w.seconds) * 1000,
  text: w.texts["qwen-90s"].replace(/\s+/g, " ").trim() }));
const supersededBy = new Map(lines0.filter(x => reruns.some(r => x.startMs >= r.startMs && x.startMs < r.endMs))
  .map(x => [x.alias, reruns.find(r => x.startMs >= r.startMs && x.startMs < r.endMs).alias]));
const lines1 = [...lines0.filter(x => !supersededBy.has(x.alias)), ...reruns].sort((a, b) => a.startMs - b.startMs);
const before = noteWriter(ctx, model, lines0);
const after = noteWriter(ctx, model, lines1);

// Names a correct note of the rerun windows should use, and the captions' mishearings (see poc-asr-context.mjs).
const canonical = { ntdll: /ntdll/i, WinDbg: /win ?dbg|win debugger|windows debugger/i, "symbol server": /symbol server/i,
  ntoskrnl: /ntoskrnl/i, "Zw prefix": /\bzw/i, NtCreateFile: /NtCreateFile/i, lkd: /\blkd\b/i };
const misheard = { needle: /needle/i, Nptel: /nptel/i, "green/Kevin debugger": /green debugger|kevin debugger/i, "sample server": /sample server/i,
  "set w": /set ?w\b/i, LXD: /\bLXD\b/, "OS and kernel": /os and kernel/i, "query file": /query ?file/i, "grave file": /grave file/i };
const terms = text => ({ canonical: Object.entries(canonical).filter(([, re]) => re.test(text)).map(([k]) => k),
  misheard: Object.entries(misheard).filter(([, re]) => re.test(text)).map(([k]) => k) });

const editSchema = { type: "object", required: ["edits"], properties: { edits: { type: "array", items: { type: "object",
  required: ["find", "replace", "reason"], properties: { find: { type: "string" }, replace: { type: "string" }, reason: { type: "string" } } } } } };
const regenerateSchema = { type: "object", required: ["markdown"], properties: { markdown: { type: "string" } } };
const norm = name => name.toLowerCase().replace(/[\s_\-().]/g, "");

try {
  const course = await courseContext(ctx, model, out);
  const prefix = JSON.stringify({ course });
  const segments0 = readJson(path.join(base, "segments.json"));
  const outline0 = readJson(path.join(base, "outline.json"));
  const polished0 = new Map(Object.entries(readJson(path.join(base, "polished.json"))));
  const concepts0 = readJson(path.join(base, "concept-graph.json"));
  const blocks0 = before.blocksOf(segments0);
  const topicOf = new Map(outline0.topics.flatMap(t => t.blocks.map(id => [id, t.key])));
  const keys = [...after.windows().keys()];
  if (JSON.stringify(keys) !== JSON.stringify([...before.windows().keys()])) throw new Error("The rerun changed the five-minute windows");

  // ── Scenario 1a: graph update ──
  // Lineage: a segment is affected when one of its sources was superseded.
  const affected = [...new Set([...supersededBy.keys()].map(alias => keys.indexOf(Math.floor(lines0.find(x => x.alias === alias).startMs / 300_000))))].sort((a, b) => a - b);
  const segmentsG = segments0.map(s => s);
  for (const p of affected)
    segmentsG[p] = await after.segment(prefix, p, after.windows().get(keys[p]), segmentsG.slice(0, p), { stage: "graph-segment", idPrefix: "u" });
  const blocksG = after.blocksOf(segmentsG);
  // Each rewritten segment's blocks go into the topic that held most of its old blocks, where its first old block was.
  const topicsG = outline0.topics.map(t => ({ ...t, blocks: [...t.blocks] }));
  const changedTopics = new Set();
  for (const p of affected) {
    const oldIds = segments0[p].blocks.map(b => b.id);
    const counts = Map.groupBy(oldIds.filter(id => topicOf.has(id)), id => topicOf.get(id));
    const target = [...counts.entries()].sort((a, b) => b[1].length - a[1].length)[0][0];
    for (const t of topicsG) if (t.blocks.some(id => oldIds.includes(id))) changedTopics.add(t.key);
    const host = topicsG.find(t => t.key === target);
    const at = host.blocks.findIndex(id => oldIds.includes(id));
    for (const t of topicsG) t.blocks = t.blocks.filter(id => !oldIds.includes(id));
    host.blocks.splice(Math.max(0, at), 0, ...segmentsG[p].blocks.map(b => b.id));
  }
  const emptied = topicsG.filter(t => t.blocks.length === 0 && !topicsG.some(c => c.parent === t.key)).map(t => t.key);
  const topicsGraph = topicsG.filter(t => !emptied.includes(t.key));

  // Concept graph: concepts the rewrite removed or added. Other topics mentioning them (by name in their text, or through
  // a concept node's blocks) are checked against the corrected notes.
  const namesOf = segs => new Map(segs.flatMap(s => s.concepts).map(c => [norm(c.name), c.name]));
  const oldNames = namesOf(affected.map(p => segments0[p])), newNames = namesOf(affected.map(p => segmentsG[p]));
  const removed = [...oldNames].filter(([id]) => !newNames.has(id)).map(([, name]) => name);
  const added = [...newNames].filter(([id]) => !oldNames.has(id)).map(([, name]) => name);
  const outside = outline0.topics.filter(t => !changedTopics.has(t.key) && polished0.has(t.key));
  const viaGraph = new Map();
  for (const node of concepts0.nodes.filter(n => removed.concat(added).some(name => norm(name) === n.id)))
    for (const id of node.blocks) {
      const key = topicOf.get(id);
      if (key && !changedTopics.has(key)) viaGraph.set(key, [...(viaGraph.get(key) ?? []), node.name]);
    }
  const viaText = new Map(outside.map(t => [t.key, removed.filter(name => (polished0.get(t.key) ?? "").toLowerCase().includes(name.toLowerCase()))])
    .filter(([, names]) => names.length > 0));
  const candidates = [...new Set([...viaGraph.keys(), ...viaText.keys()])].slice(0, 6);
  const corrected = affected.flatMap(p => segmentsG[p].blocks.map(b => b.markdown)).join("\n\n");
  const polishedG = new Map(polished0);
  const edits = [];
  await pool(candidates, 4, async key => {
    const topic = outline0.topics.find(t => t.key === key);
    const text = polishedG.get(key) ?? "";
    const reply = await llm(ctx, { model, stage: "graph-consistency", label: `consistency ${topic.title}`, schema: editSchema, maxOutputTokens: 4096,
      system: `Part of a lecture was recognized again from its audio, and the notes for that part were rewritten. "corrected" is ` +
        `the rewritten notes; "removedConcepts" and "addedConcepts" are concept names that changed. "topic" is another part of ` +
        `the same notes. Return edits only where the topic contradicts the corrected notes or still uses a name the correction ` +
        `replaced (for example a misrecognized function or tool name). find is copied exactly from topic.markdown and occurs ` +
        `there once; replace keeps every citation [T…] inside find. Return no edits when nothing conflicts; never restyle or ` +
        `shorten.\n` + style,
      text: prefix + "\n" + JSON.stringify({ removedConcepts: removed, addedConcepts: added, corrected, topic: { title: topic.title, markdown: text } }) });
    let updated = text;
    for (const e of reply.edits) {
      const once = e.find.length > 0 && updated.split(e.find).length === 2;
      const keepsCitations = citations(e.find).every(x => citations(e.replace).includes(x));
      if (once && keepsCitations) updated = updated.replace(e.find, e.replace);
      edits.push({ topic: topic.title, found: viaGraph.has(key) ? "concept graph" : "text", ...e, applied: once && keepsCitations });
    }
    polishedG.set(key, updated);
  });
  // Polish only the top-level topics that hold a changed topic.
  const top = key => { let t = topicsGraph.find(x => x.key === key); while (t?.parent) t = topicsGraph.find(x => x.key === t.parent); return t; };
  const tops = [...new Set([...changedTopics].map(top).filter(Boolean))];
  const { polished: repolished } = await after.polish(prefix, topicsGraph, blocksG, tops, "graph-polish");
  for (const [key, text] of repolished) polishedG.set(key, text);
  for (const key of emptied) polishedG.delete(key);
  const conceptsG = after.conceptGraph(segmentsG, blocksG);
  const notesGraph = after.render("# Week 3 筆記（POC：graph 更新）", topicsGraph, polishedG, { ...outline0, topics: topicsGraph }, conceptsG, segmentsG, blocksG);
  writeFileSync(path.join(out, "notes-graph.md"), notesGraph);

  // ── Scenario 1b: full rebuild ──
  const segmentsR = await after.segments(prefix, "rebuild-segment");
  const blocksR = after.blocksOf(segmentsR);
  const outlineR = await after.outline(prefix, blocksR, segmentsR, "rebuild-final");
  const { polished: polishedR } = await after.polish(prefix, outlineR.topics, blocksR, undefined, "rebuild-final");
  const conceptsR = after.conceptGraph(segmentsR, blocksR);
  const notesRebuild = after.render("# Week 3 筆記（POC：完整重建）", outlineR.topics, polishedR, outlineR, conceptsR, segmentsR, blocksR);
  writeFileSync(path.join(out, "notes-rebuild.md"), notesRebuild);

  // ── Scenario 2: regenerate one topic ──
  const target = outline0.topics.find(t => /\bZw/.test(polished0.get(t.key) ?? ""));
  if (!target) throw new Error("No topic in the saved notes mentions Zw; choose another topic to regenerate");
  const instruction = "重寫這個主題：加入一個比較 Nt* 與 Zw* 函式的表格（前綴、面向的模式、位址、實際程式碼位置），" +
    "並把課堂其他部分講到的相關內容（例如 SSDT、SSN、ntoskrnl.exe）以「另見」交叉引用補充。";
  const own = polished0.get(target.key);
  const ownAliases = new Set(before.citedAliases(own));
  const linked = new Map();
  for (const node of concepts0.nodes.filter(n => n.blocks.some(id => target.blocks.includes(id))))
    for (const id of node.blocks.filter(id => !target.blocks.includes(id))) linked.set(id, (linked.get(id) ?? 0) + 1);
  const related = [...linked.entries()].sort((a, b) => b[1] - a[1]).slice(0, 10).map(([id]) => blocks0.find(b => b.id === id)).filter(Boolean);
  const regenerate = async (label, relatedBlocks) => {
    const aliases = new Set([...ownAliases, ...relatedBlocks.flatMap(b => before.citedAliases(b.markdown))]);
    const reply = await llm(ctx, { model, stage: "regenerate", label, schema: regenerateSchema, maxOutputTokens: 16384,
      system: `Rewrite one topic of lecture notes as its owner asks. Input: instruction, topic (title and current markdown), ` +
        `transcript (the lines its citations and the related notes cite, [alias, time, text])` +
        (relatedBlocks.length ? `, related (notes from other parts of the same lecture that share its concepts)` : "") +
        `. Keep every fact, number, command, "(?)" and citation; add only what the transcript supports. The topic title is ` +
        `already a heading: no Markdown headings.\n` + style,
      text: prefix + "\n" + JSON.stringify({ instruction, topic: { title: target.title, markdown: own },
        ...(relatedBlocks.length ? { related: relatedBlocks.map(b => b.markdown) } : {}),
        transcript: [...aliases].map(a => before.byAlias.get(a)).filter(Boolean).sort((x, y) => x.startMs - y.startMs).map(before.row) }) });
    const stats = { invalid: 0 };
    return { markdown: keepCitations(reply.markdown, aliases, stats), invalid: stats.invalid };
  };
  const plain = await regenerate(`regenerate ${target.title}`, []);
  const graph = await regenerate(`regenerate ${target.title} + graph`, related);
  writeFileSync(path.join(out, "regenerate-plain.md"), `## ${target.title}\n\n${before.cite(plain.markdown)}\n`);
  writeFileSync(path.join(out, "regenerate-graph.md"), `## ${target.title}\n\n${before.cite(graph.markdown)}\n`);

  // ── Report ──
  writeJson(path.join(out, "edits.json"), edits);
  writeJson(path.join(out, "calls.json"), ctx.ledger.calls);
  const stage = names => names.map(s => ctx.ledger.stage(s)).reduce((n, s) => ({ calls: n.calls + s.calls, inputTokens: n.inputTokens + s.inputTokens,
    outputTokens: n.outputTokens + s.outputTokens, costUsd: n.costUsd + s.costUsd,
    seconds: n.seconds + ctx.ledger.calls.filter(x => x.stage === s.stage).reduce((m, x) => m + (x.latencyMs ?? 0), 0) / 1000 }),
    { calls: 0, inputTokens: 0, outputTokens: 0, costUsd: 0, seconds: 0 });
  const g = stage(["graph-segment", "graph-consistency", "graph-polish"]), r = stage(["rebuild-segment", "rebuild-final"]);
  // Stability: how much of the notes outside the changed topics the reader still finds word for word.
  const unchangedTopics = outline0.topics.filter(t => !changedTopics.has(t.key) && polished0.has(t.key));
  const baseLines = unchangedTopics.flatMap(t => before.cite(polished0.get(t.key)).split("\n").map(x => x.trim()).filter(x => x.length > 20));
  const kept = notes => baseLines.filter(x => notes.includes(x)).length;
  const identical = polished => unchangedTopics.filter(t => polished.get(t.key) === polished0.get(t.key)).length;
  // Superseded caption lines still cited by the notes' topics and summary (read before citations become times).
  const staleCitations = texts => new Set(texts.flatMap(before.citedAliases).filter(x => supersededBy.has(x))).size;
  const range = segs => affected.flatMap(p => segs[p].blocks.map(b => b.markdown)).join("\n");
  const rangeTerms = { base: terms(range(segments0)), graph: terms(range(segmentsG)), rebuild: terms(range(segmentsR)) };
  const fmt = t => `${t.canonical.join(", ") || "—"} | ${t.misheard.join(", ") || "—"}`;
  const usd = x => "$" + x.toFixed(4);
  const outsideMs = new Set([...ownAliases].map(a => before.byAlias.get(a).startMs));
  const [lo, hi] = [Math.min(...outsideMs), Math.max(...outsideMs)];
  const crossRefs = text => new Set(before.citedAliases(text).filter(a => { const t = before.byAlias.get(a).startMs; return t < lo - 300_000 || t > hi + 300_000; })).size;
  const report = [
    `# Note update POC — Week 3, ${model}`, "",
    `Change: Qwen3-ASR 90-second rerun of ${reruns.map(x => clock(x.startMs)).join(", ")} supersedes ${supersededBy.size} caption lines ` +
      `(${[...new Set(supersededBy.values())].length} new sources). Lineage marks segments ${affected.map(p => clock(segments0[p].start)).join(", ")} as affected.`, "",
    "| | Graph update | Full rebuild |", "| --- | --- | --- |",
    `| Provider calls | ${g.calls} (segments ${ctx.ledger.stage("graph-segment").calls}, consistency ${ctx.ledger.stage("graph-consistency").calls}, polish ${ctx.ledger.stage("graph-polish").calls}) | ${r.calls} (segments ${ctx.ledger.stage("rebuild-segment").calls}, final ${ctx.ledger.stage("rebuild-final").calls}) |`,
    `| Input / output tokens | ${g.inputTokens.toLocaleString()} / ${g.outputTokens.toLocaleString()} | ${r.inputTokens.toLocaleString()} / ${r.outputTokens.toLocaleString()} |`,
    `| Cost | ${usd(g.costUsd)} | ${usd(r.costUsd)} |`,
    `| Provider time (sum of calls) | ${g.seconds.toFixed(0)} s | ${r.seconds.toFixed(0)} s |`,
    `| Topics outside the change left byte-identical | ${identical(polishedG)} / ${unchangedTopics.length} | ${identical(polishedR)} / ${unchangedTopics.length} (different outline) |`,
    `| Lines outside the change still present word for word | ${kept(notesGraph)} / ${baseLines.length} | ${kept(notesRebuild)} / ${baseLines.length} |`,
    `| Topics (top level) | ${topicsGraph.length} (${topicsGraph.filter(t => !t.parent).length}) | ${outlineR.topics.length} (${outlineR.topics.filter(t => !t.parent).length}) |`,
    `| Superseded lines still cited (topics; summary) | ${staleCitations(topicsGraph.map(t => polishedG.get(t.key) ?? ""))}; ${staleCitations(outline0.summary)} ` +
      `(before: ${staleCitations([...polished0.values()])}) | ${staleCitations([...polishedR.values()])}; ${staleCitations(outlineR.summary)} |`, "",
    "Notes of the affected segments — names found | caption mishearings found:", "",
    `- before: ${fmt(rangeTerms.base)}`, `- graph update: ${fmt(rangeTerms.graph)}`, `- rebuild: ${fmt(rangeTerms.rebuild)}`, "",
    `Concepts the rewrite removed: ${removed.join(", ") || "none"}; added: ${added.join(", ") || "none"}.`,
    `Topics checked for consistency: ${candidates.map(k => outline0.topics.find(t => t.key === k).title + (viaGraph.has(k) ? " (concept graph: " + [...new Set(viaGraph.get(k))].join(", ") + ")" : " (text)")).join("; ") || "none"}.`,
    `Edits proposed ${edits.length}, applied ${edits.filter(e => e.applied).length}:`,
    ...edits.map(e => `- ${e.topic}: "${e.find}" → "${e.replace}" (${e.reason})${e.applied ? "" : " — not applied"}`), "",
    `## Regenerate "${target.title}" on request`, "", `Instruction: ${instruction}`, "",
    "| | Topic only | Topic + concept graph |", "| --- | --- | --- |",
    `| Related blocks given | 0 | ${related.length} |`,
    `| Characters | ${plain.markdown.length} | ${graph.markdown.length} |`,
    `| Table | ${/\|\s*:?-{3}/.test(plain.markdown) ? "yes" : "no"} | ${/\|\s*:?-{3}/.test(graph.markdown) ? "yes" : "no"} |`,
    `| Sources cited more than five minutes outside the topic | ${crossRefs(plain.markdown)} | ${crossRefs(graph.markdown)} |`,
    `| Invalid citations removed | ${plain.invalid} | ${graph.invalid} |`, "",
    `Cost: ${usd(ctx.ledger.stage("regenerate").costUsd)}. Cached replies reused: ${ctx.ledger.calls.filter(x => x.cached).length}; spent this run: ${usd(ctx.ledger.spent)}.`
  ].join("\n");
  writeFileSync(path.join(out, "report.md"), report);
  console.log(report);
} catch (error) {
  writeJson(path.join(out, "calls.json"), ctx.ledger.calls);
  if (error instanceof OfflineMiss) { console.log(error.message); process.exitCode = 2; }
  else { console.error(error.stack ?? error.message); process.exitCode = 1; }
  console.log(`Spent this run: $${ctx.ledger.spent.toFixed(4)}`);
}
