import DOMPurify from "dompurify";

let rendererLoading: Promise<typeof import("mermaid").default> | undefined;
function loadRenderer() {
  return rendererLoading ??= import("mermaid").then(({ default: mermaid }) => {
    mermaid.initialize({ startOnLoad: false, securityLevel: "strict", theme: "neutral", htmlLabels: false, suppressErrorRendering: true });
    return mermaid;
  }).catch(() => {
    rendererLoading = undefined;
    throw new Error("Diagram renderer could not load. Save or copy your draft, then reload to retry.");
  });
}

type Cached = { svg?: string; bytes: number };
type Job = { source: string; users: Set<symbol>; promise: Promise<string>; resolve: (svg: string) => void; reject: (error: Error) => void; started: boolean };
const cache = new Map<string, Cached>();
const pending = new Map<string, Job>();
const queue: Job[] = [];
let rendering = false, cacheBytes = 0;
const cacheBudget = 2 * 1024 * 1024;

function remember(source: string, svg?: string) {
  const bytes = 2 * (source.length + (svg?.length ?? 0));
  // Oversized successful diagrams may be displayed without retaining another cached copy.
  if (bytes > cacheBudget / 2) return;
  const previous = cache.get(source);
  if (previous) { cacheBytes -= previous.bytes; cache.delete(source); }
  cache.set(source, { svg, bytes }); cacheBytes += bytes;
  while (cache.size > 32 || cacheBytes > cacheBudget) {
    const key = cache.keys().next().value!; cacheBytes -= cache.get(key)!.bytes; cache.delete(key);
  }
}
async function drain() {
  if (rendering) return;
  rendering = true;
  try {
    while (queue.length) {
      const job = queue.shift()!; job.started = true;
      // This host belongs to this request. Mermaid can throw before removing its own
      // temporary SVG (e.g. an unfinished live draft); always remove the whole host.
      const host = document.createElement("div"); host.className = "diagram-render-host";
      host.setAttribute("aria-hidden", "true"); host.inert = true;
      host.style.cssText = "position:absolute;left:-100000px;top:0;visibility:hidden;pointer-events:none";
      document.body.append(host);
      let rendererReady = false;
      try {
        const mermaid = await loadRenderer(); rendererReady = true;
        const result = await mermaid.render("diagram-" + crypto.randomUUID().replaceAll("-", ""), job.source, host);
        if (result.svg.length > 1024 * 1024) throw new Error("Diagram output budget exceeded");
        const svg = DOMPurify.sanitize(result.svg, { USE_PROFILES: { html: true, svg: true, svgFilters: true } });
        if (job.users.size) remember(job.source, svg);
        job.resolve(svg);
      } catch (error) {
        // Retain a small failure marker, never an Error/DOM/parser graph, so an
        // unchanged invalid draft is not parsed again on each preview mount.
        if (rendererReady && job.users.size) remember(job.source);
        job.reject(rendererReady ? new Error("Diagram syntax needs review.") : error as Error);
      } finally { host.remove(); if (pending.get(job.source) === job) pending.delete(job.source); }
    }
  } finally { rendering = false; }
}
export function requestDiagram(source: string): { promise: Promise<string>; cancel: () => void } {
  const cached = cache.get(source);
  if (cached) {
    cache.delete(source); cache.set(source, cached);
    return { promise: cached.svg === undefined ? Promise.reject(new Error("Diagram syntax needs review.")) : Promise.resolve(cached.svg), cancel: () => {} };
  }
  if (source.length > 24000 || !pending.has(source) && pending.size >= 8)
    return { promise: Promise.reject(new Error("Diagram render budget exceeded")), cancel: () => {} };
  let job = pending.get(source);
  if (!job) {
    let resolve!: Job["resolve"], reject!: Job["reject"];
    const promise = new Promise<string>((yes, no) => { resolve = yes; reject = no; });
    job = { source, promise, resolve, reject, users: new Set(), started: false }; pending.set(source, job); queue.push(job);
  }
  const selected = job, user = Symbol(); selected.users.add(user); void drain();
  return { promise: selected.promise, cancel: () => {
    selected.users.delete(user);
    if (!selected.users.size && !selected.started) {
      queue.splice(queue.indexOf(selected), 1); pending.delete(source); selected.reject(new Error("Diagram render cancelled"));
    }
  } };
}
