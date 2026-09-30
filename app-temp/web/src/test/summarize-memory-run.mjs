// Offline metrics only. Raw results are preserved, including legacy label mistakes.
import { readFile, writeFile } from "node:fs/promises";
import assert from "node:assert/strict";
const folder = process.argv[2]; assert(folder?.startsWith("output/playwright/frontend-memory/"));
const data = JSON.parse(await readFile(`${folder}/results.json`, "utf8"));
const metrics = data.metrics, soak = metrics.filter(x => x.label.startsWith("soak-")), gc = soak.filter(x => x.collected);
const median = values => { const sorted = [...values].sort((a, b) => a - b); return sorted.length ? sorted[Math.floor(sorted.length / 2)] : null; };
const range = values => values.length ? { min: Math.min(...values), max: Math.max(...values), median: median(values) } : null;
const mib = value => +(value / 1048576).toFixed(2);
const first = soak[0], last = soak.at(-1), seconds = first && last ? (last.elapsedMs - first.elapsedMs) / 1000 : 0;
const small = metrics.findLast(x => x.label === "small-after-soak");
let retained; try { retained = JSON.parse(await readFile(`${folder}/heap-analysis.json`, "utf8")); } catch {}
const summary = { browser: data.browser, source: data.naturalInput, initialTranscriptCount: data.initialCounts.longTranscripts,
  initialDataHours: data.naturalInput?.seconds / 3600 || data.fixtureDataHours, elapsedIncludingFinalChecksSeconds: data.realSoakSeconds,
  cycles: data.cycles, finalTranscriptCount: data.initialCounts.longTranscripts + metrics.at(-1).finalCount,
  soakGcHeapMiB: range(gc.map(x => mib(x.heapAfter))), firstFiveGcMedianMiB: median(gc.slice(0, 5).map(x => mib(x.heapAfter))),
  lastFiveGcMedianMiB: median(gc.slice(-5).map(x => mib(x.heapAfter))), soakMountedRows: range(soak.map(x => x.rows)), soakElements: range(soak.map(x => x.elements)),
  maximumOrphanDiagrams: Math.max(...metrics.map(x => x.orphanDiagrams)),
  rendererTaskTimePercent: seconds ? +(100 * (last.taskDuration - first.taskDuration) / seconds).toFixed(2) : null,
  rendererScriptTimePercent: seconds ? +(100 * (last.scriptDuration - first.scriptDuration) / seconds).toFixed(2) : null,
  rendererLayoutTimePercent: seconds ? +(100 * (last.layoutDuration - first.layoutDuration) / seconds).toFixed(2) : null,
  smallSession: small && { heapMiB: mib(small.heapAfter), rows: small.rows, elements: small.elements, documents: small.documents, nodes: small.nodes, listeners: small.jsEventListeners,
    oldSourceIdStrings: retained?.sourceIdStringsStillPresent, abortSignals: retained?.abortSignals, networkResourceRecords: retained?.networkResourceRecords },
  payloads: Object.entries(data.payloads).map(([path, bytes]) => ({ path, reads: data.requests[path], totalBytes: bytes, meanBytes: Math.round(bytes / data.requests[path]) })),
  interimMounted: data.interimMounted, wheelWorked: data.wheelWorked, unselectedRecordingSyncs: data.unselectedRecordingSyncs, errors: data.errors, writes: data.writes,
  limitations: "Fixture wire payload, renderer main-thread time and JS heap, not actual DB/provider bytes, process CPU or whole-browser RSS. Initial hours derive from naturalInput; older raw files may have a legacy fixtureDataHours=3 label." };
assert.deepEqual(data.errors, []); assert.deepEqual(data.writes, []); assert.equal(summary.maximumOrphanDiagrams, 0);
if (process.argv.includes("--one-hour")) assert(data.realSoakSeconds >= 3600, "The run did not reach one hour");
await writeFile(`${folder}/summary.json`, JSON.stringify(summary, null, 2)); console.log(JSON.stringify(summary, null, 2));
