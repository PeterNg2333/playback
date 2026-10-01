// Distilled heap evidence only; snapshots remain local ignored artifacts.
import { readFile, writeFile } from "node:fs/promises";
import assert from "node:assert/strict";
const folder = process.argv[2];
if (!folder?.startsWith("output/playwright/frontend-memory/"))
  throw new Error("Use a local frontend-memory run folder");
const snapshot = JSON.parse(
  await readFile(`${folder}/small-after-soak.heapsnapshot`, "utf8"),
);
const {
  node_fields: fields,
  node_types: types,
  edge_fields: edgeFields,
  edge_types: edgeTypes,
} = snapshot.snapshot.meta;
const width = fields.length,
  ew = edgeFields.length,
  ni = fields.indexOf("name"),
  ti = fields.indexOf("type"),
  ec = fields.indexOf("edge_count"),
  si = fields.indexOf("self_size");
const ei = edgeFields.indexOf("to_node"),
  et = edgeFields.indexOf("type"),
  en = edgeFields.indexOf("name_or_index");
const nodes = snapshot.nodes,
  edges = snapshot.edges,
  count = nodes.length / width;
const heads = new Int32Array(count).fill(-1),
  next = new Int32Array(edges.length / ew).fill(-1),
  from = new Int32Array(edges.length / ew);
const native = new Map();
let sourceIds = 0,
  target,
  nativeTarget;
const nativeName = process.argv
  .find((x) => x.startsWith("--native="))
  ?.slice(9);
for (let node = 0, edge = 0; node < nodes.length; node += width) {
  const type = types[ti][nodes[node + ti]],
    name = snapshot.strings[nodes[node + ni]];
  if (type === "string" && /^memory-(?:final-)?\d+$/.test(name)) {
    sourceIds++;
    if (target === undefined || name === "memory-2699") target = node;
  }
  if (type === "native") {
    const bucket = native.get(name) ?? { name, count: 0, bytes: 0 };
    bucket.count++;
    bucket.bytes += nodes[node + si];
    native.set(name, bucket);
    if (nativeName === name && nativeTarget === undefined) nativeTarget = node;
  }
  for (let end = edge + nodes[node + ec] * ew; edge < end; edge += ew) {
    const index = edge / ew,
      to = edges[edge + ei] / width;
    from[index] = node;
    next[index] = heads[to];
    heads[to] = index;
  }
}
function pathToRoot(target) {
  if (target === undefined) return [];
  const links = new Int32Array(count).fill(-1),
    visited = new Uint8Array(count),
    queue = [target];
  visited[target / width] = 1;
  let found;
  for (let i = 0; i < queue.length && queue.length < 200000; i++) {
    const to = queue[i];
    if (to === 0) {
      found = to;
      break;
    }
    for (let edge = heads[to / width]; edge >= 0; edge = next[edge]) {
      if (edgeTypes[et][edges[edge * ew + et]] === "weak") continue;
      const source = from[edge];
      if (visited[source / width]) continue;
      visited[source / width] = 1;
      links[source / width] = edge;
      queue.push(source);
    }
  }
  if (found === undefined)
    return [{ error: "No root path found within the budget" }];
  const result = [];
  for (let node = found; node !== target; ) {
    const edge = links[node / width],
      type = types[ti][nodes[node + ti]],
      name = snapshot.strings[nodes[node + ni]];
    const edgeType = edgeTypes[et][edges[edge * ew + et]],
      edgeName = ["element", "hidden"].includes(edgeType)
        ? edges[edge * ew + en]
        : snapshot.strings[edges[edge * ew + en]];
    result.push({
      type,
      name: type === "string" ? `<string ${name.length} chars>` : name,
      edge: edgeName,
    });
    node = edges[edge * ew + ei];
  }
  return result;
}
const report = {
  sourceIdStringsStillPresent: sourceIds,
  sourceIdRootPath: pathToRoot(target),
  abortSignals: native.get("AbortSignal")?.count ?? 0,
  ...(nativeName
    ? { inspectedNative: nativeName, nativeRootPath: pathToRoot(nativeTarget) }
    : {}),
  networkResourceRecords:
    native.get("blink::NetworkResourcesData::ResourceData")?.count ?? 0,
  native: [...native.values()].sort((a, b) => b.bytes - a.bytes).slice(0, 25),
};
await writeFile(
  `${folder}/heap-analysis.json`,
  JSON.stringify(report, null, 2),
);
console.log(JSON.stringify(report, null, 2));
if (process.argv.includes("--assert-no-old-session"))
  assert.equal(sourceIds, 0, "The old 2700-source session is still reachable");
