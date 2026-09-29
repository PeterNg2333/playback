// Offline report builder. --snapshot only reads the isolated localhost API.
import assert from "node:assert/strict";
import { readFile, writeFile, mkdir } from "node:fs/promises";
const folder = "app-temp/data/validation/runs/2026-09-29-week3";
await mkdir(folder, { recursive: true });
const read = async file => { try { return JSON.parse((await readFile(file, "utf8")).replace(/^\uFEFF/, "")); } catch (error) { if (error.code === "ENOENT") return null; throw error; } };
const saved = await read(`${folder}/provider-results.json`);
if (process.argv.includes("--snapshot")) {
  const fixture = await read(`${folder}/db-session.json`);
  assert.match(fixture.id, /^[a-f0-9]{32}$/);
  const get = async endpoint => {
    const response = await fetch("http://127.0.0.1:5081/api" + endpoint, { signal: AbortSignal.timeout(10000), redirect: "error" });
    assert.equal(response.status, 200, endpoint); return response.json();
  };
  const health = await get("/health");
  assert.equal(health.database, "playback_e2e"); assert.equal(health.mongo, true);
  assert.equal(health.autoNotes, false); assert.equal(health.autoTerms, false); assert.equal(health.asrPaused, true);
  const [session, records, conversations, flow] = await Promise.all([
    get(`/sessions/${fixture.id}`), get(`/sessions/${fixture.id}/activity`),
    get(`/sessions/${fixture.id}/conversations`), get(`/groups/${fixture.groupId}/flow?sessionId=${fixture.id}`),
  ]);
  const savedConversations = await Promise.all(conversations.map(x => {
    assert.match(x.id, /^[a-f0-9]{32}$/); return get(`/sessions/${fixture.id}/conversations/${x.id}`);
  }));
  await writeFile(`${folder}/application-results.json`, JSON.stringify({ capturedAt: new Date().toISOString(),
    evidence: "Read-only snapshot of real API/Mongo records; no new AI calls.",
    source: saved?.source, health, session, records, conversations, savedConversations, flow }, null, 2));
}
const application = await read(`${folder}/application-results.json`);
function usage(value) { if (!value) return null; try { return typeof value === "string" ? JSON.parse(value) : value; } catch { return null; } }
function tokens(data, names) {
  if (!data) return null;
  for (const name of names) if (Number.isFinite(data[name]) && data[name] >= 0) return data[name];
  return null;
}
const byId = new Map();
for (const [evidence, records] of [["initial in-memory provider run", saved?.records ?? []], ["real API/Mongo execution", application?.records ?? []]])
  for (const x of records) {
    const key = `${x.sessionId ?? evidence}:${x.id}`;
    if (!x.id || byId.has(key)) continue;
    byId.set(key, { id: x.id, sessionId: x.sessionId, task: x.task, provider: x.provider, model: x.model, status: x.status,
      usage: usage(x.usageJson), evidence, inputBytes: x.inputBytes, promptVersion: x.promptVersion, promptHash: x.promptHash,
      inputHash: x.inputHash, providerLatencyMs: x.providerLatencyMs, summary: x.summary, startedAt: x.startedAt });
  }
const rows = [...byId.values()];
for (const [index, x] of (saved?.direct ?? []).entries()) rows.push({ id: `direct-${index}`, task: x.task,
  provider: x.task.startsWith("gate-") || x.task === "term-rank" ? "typesafe" : "vertex",
  model: x.result?.model ?? x.rank?.model ?? saved.model, status: x.status ?? "completed",
  usage: usage(x.usageJson ?? x.result?.usageJson ?? x.rank?.usage), evidence: "initial direct provider check",
  promptVersion: x.promptVersion, inputHash: x.inputHash, providerLatencyMs: x.providerLatencyMs ?? x.result?.latencyMs ?? x.rank?.latencyMs });
for (const row of rows) {
  row.inputTokens = tokens(row.usage, ["InputTokenCount", "inputTokenCount", "input_tokens", "promptTokenCount"]);
  row.outputTokens = tokens(row.usage, ["OutputTokenCount", "outputTokenCount", "output_tokens", "candidatesTokenCount"]);
  row.cachedInputTokens = tokens(row.usage, ["CachedInputTokenCount", "cachedInputTokenCount", "cachedContentTokenCount"]);
  row.estimatedUsd = row.provider === "typesafe" && row.model === "jev-1.13.0" && row.inputTokens !== null ? row.inputTokens * .042 / 1e6 :
    row.provider.startsWith("vertex") && row.model === "gemini-3.1-flash-lite" && row.inputTokens !== null && row.outputTokens !== null ?
      (row.inputTokens * .25 + row.outputTokens * 1.5) / 1e6 : null;
  row.cacheAdjustedEstimateUsd = row.estimatedUsd;
  if (row.provider.startsWith("vertex") && row.estimatedUsd !== null && row.cachedInputTokens !== null && row.cachedInputTokens <= row.inputTokens)
    row.cacheAdjustedEstimateUsd = ((row.inputTokens - row.cachedInputTokens) * .25 + row.cachedInputTokens * .025 + row.outputTokens * 1.5) / 1e6;
}
const known = rows.filter(x => x.estimatedUsd !== null), missing = rows.filter(x => x.estimatedUsd === null && x.status !== "cache-hit");
const sourceStatuses = application?.session.transcripts.reduce((out, x) => { out[x.noteStatus] = (out[x.noteStatus] ?? 0) + 1; return out; }, {});
const noteCalls = rows.filter(x => x.task === "Note revision"), organizerCalls = rows.filter(x => x.task === "Section organization");
const counts = items => ({ total: items.length, completed: items.filter(x => x.status === "completed").length, failed: items.filter(x => x.status === "failed").length });
const searches = (application?.savedConversations ?? []).flatMap(x => (x.turns ?? []).map(turn => turn.answer))
  .filter(x => x?.webGroundingMetadataJson && x?.webSearchQueries?.length)
  .map(x => ({ questionId: x.questionId, reportedQueries: x.webSearchQueries.length, hasWebSources: x.evidence.some(e => e.url) }));
const grounding = { recordedSearches: searches, reportedQueries: searches.reduce((sum, x) => sum + x.reportedQueries, 0),
  usdPerThousandQueriesAboveMonthlyIncluded: 14, monthlyIncludedQueriesAllGemini3: 5000,
  monthlyQuotaRemaining: "unknown", billedQueryCount: "not verified with account billing",
  fullyChargedReportedQueryEstimateUsd: searches.length ? searches.reduce((sum, x) => sum + x.reportedQueries, 0) * 14 / 1000 : null,
  limitation: "Metadata query count is recorded, not an invoice. Monthly shared quota remaining and query metadata for the initial rejected detail call are unknown." };
const report = { generatedAt: new Date().toISOString(), source: saved?.source, applicationCapturedAt: application?.capturedAt, rows,
  knownTokenEstimateUsd: known.length ? known.reduce((sum, x) => sum + x.estimatedUsd, 0) : null,
  knownCacheAdjustedEstimateUsd: known.length ? known.reduce((sum, x) => sum + x.cacheAdjustedEstimateUsd, 0) : null,
  callsWithUsableUsage: known.length, callsWithoutUsableUsage: missing.length, validationErrors: saved?.errors ?? [],
  noteCalls: counts(noteCalls), organizerCalls: counts(organizerCalls), noteVersion: application?.session.noteVersion, sourceStatuses, grounding,
  status: application ? "Real provider/Mongo evidence saved; full-hour note quality is not yet passed" : "API/Mongo snapshot missing",
  pricing: { ...saved?.cost, vertexGlobalUsdPerMillionCachedInput: .025 },
  limitations: "Not an invoice. Includes failed calls with saved usage; missing usage is not zero. Global standard Vertex text rates, and a separate estimate applying only reported cached input. Recorded Search queries have a separate fully-charged scenario; monthly quota remaining and rejected-detail query metadata are unknown. Non-global surcharge, credits and unknown usage excluded. No fresh ASR uploads. Validation errors are not invented provider calls." };
await writeFile(`${folder}/cost-summary.json`, JSON.stringify(report, null, 2));
const md = ["# Week 3 provider cost and coverage", "", `Status: ${report.status}.`, "",
  `Usable token estimates: ${known.length}; unknown usage records: ${missing.length}.`,
  `Known token estimate: ${report.knownTokenEstimateUsd === null ? "unavailable" : "USD " + report.knownTokenEstimateUsd.toFixed(6)}.`, "",
  `With reported cached input: ${report.knownCacheAdjustedEstimateUsd === null ? "unavailable" : "USD " + report.knownCacheAdjustedEstimateUsd.toFixed(6)}.`,
  `Reported Google Search queries: ${grounding.reportedQueries}; fully-charged scenario: ${grounding.fullyChargedReportedQueryEstimateUsd === null ? "unknown" : "USD " + grounding.fullyChargedReportedQueryEstimateUsd.toFixed(6)} extra. Shared monthly included quota remaining is unknown.`,
  `Notes: ${report.noteCalls.total} attempts, ${report.noteCalls.completed} completed, ${report.noteCalls.failed} failed. Organizer: ${report.organizerCalls.total} attempts, ${report.organizerCalls.completed} completed.`,
  `Saved Mongo note version: ${report.noteVersion ?? "unknown"}; source states: ${JSON.stringify(sourceStatuses ?? {})}. Deferred is not completed.`, "",
  "| Task | Prompt | Model | State | Input tokens | Output tokens | Cached input | Standard USD |", "| --- | --- | --- | --- | ---: | ---: | ---: | ---: |",
  ...rows.map(x => `| ${x.task} | ${x.promptVersion ?? "direct"} | ${x.model} | ${x.status} | ${x.inputTokens ?? "unknown"} | ${x.outputTokens ?? "unknown"} | ${x.cachedInputTokens ?? "not reported"} | ${x.estimatedUsd?.toFixed(6) ?? "unknown"} |`), "",
  "Initial validation failures (separate from billable calls):", ...report.validationErrors.map(x => `- ${x.task}: ${x.error}`), "", report.limitations,
  "", "Prices checked 2026-09-29: [Vertex pricing](https://cloud.google.com/vertex-ai/generative-ai/pricing), [TypeSafe models](https://docs.typesafe.ai/models)."];
await writeFile(`${folder}/cost-report.md`, md.join("\n"));
console.log(JSON.stringify({ status: report.status, noteCalls: report.noteCalls, organizerCalls: report.organizerCalls, sourceStatuses,
  usableUsage: known.length, missingUsage: missing.length, knownTokenEstimateUsd: report.knownTokenEstimateUsd, knownCacheAdjustedEstimateUsd: report.knownCacheAdjustedEstimateUsd }));
