import { useSyncExternalStore } from "react";
import { ApiError, api } from "../../lib/backend/client";
import { showError } from "../../lib/store";

// Audio the browser recorded, waiting in IndexedDB until the API has it. Items go out one at a time in the order they
// were recorded, so a stream's close (and Stop's request for the after-lecture notes) follows its last chunk. A failed
// upload is retried with backoff, when the browser comes back online, and after a reload; the API recognises a retried
// chunk by session, source, sequence and hash. Only an upload the API rejects outright is dropped.
export type ChunkUpload = {
  kind: "chunk";
  sessionId: string;
  sourceId: string;
  streamId: string;
  sequence: number;
  startMs: number;
  endMs: number;
  sha256: string;
  recordedAt: string;
  wav: Blob;
};
export type StreamClose = {
  kind: "stream";
  sessionId: string;
  streamId: string;
  stop: boolean;
};
type Outgoing = ChunkUpload | StreamClose;
type Entry = { key: number; value: Outgoing };

const DATABASE = "playback-recorder";
const STORE = "outbox";
const LOCK = "playback-recorder-upload";
const RETRY_MS = [1_000, 2_000, 5_000, 10_000, 30_000];
const RETRY_QUEUED_MS = 15_000;
// Statuses that mean the API will never accept this item: bad input, a deleted session, conflicting audio, too large.
const REJECTED = new Set([400, 404, 409, 413, 422]);

// `pending` counts chunks only; `error` is the latest failure while it is retried.
type UploadState = { pending: number; error: string | null; durable: boolean };
let state: UploadState = { pending: 0, error: null, durable: true };
let queued = 0;
const listeners = new Set<() => void>();
const uploadedListeners = new Set<(sessionId: string) => void>();
// Used only when IndexedDB is unavailable; then audio is lost if the tab closes before it is sent.
const memory: Entry[] = [];
let memoryKey = 0;
let database: Promise<IDBDatabase | null> | undefined;
let draining = false;
let wake: (() => void) | null = null;

function update(next: Partial<UploadState>) {
  state = { ...state, ...next };
  for (const listener of listeners) listener();
}

function openDatabase() {
  database ??= new Promise<IDBDatabase | null>((resolve) => {
    try {
      const request = indexedDB.open(DATABASE, 1);
      request.onupgradeneeded = () =>
        request.result.createObjectStore(STORE, { autoIncrement: true });
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => resolve(null);
    } catch {
      resolve(null);
    }
  }).then((db) => {
    if (!db) update({ durable: false });
    return db;
  });
  return database;
}

function transact<T>(
  db: IDBDatabase,
  mode: IDBTransactionMode,
  initial: T,
  work: (store: IDBObjectStore, done: (value: T) => void) => void,
) {
  return new Promise<T>((resolve, reject) => {
    const transaction = db.transaction(STORE, mode);
    let result = initial;
    work(transaction.objectStore(STORE), (value) => (result = value));
    transaction.oncomplete = () => resolve(result);
    transaction.onerror = transaction.onabort = () =>
      reject(
        transaction.error ?? new Error("Saving audio in the browser failed"),
      );
  });
}

async function add(value: Outgoing) {
  const db = await openDatabase();
  if (!db) memory.push({ key: ++memoryKey, value });
  else await transact(db, "readwrite", undefined, (store) => store.add(value));
}

async function first(): Promise<Entry | null> {
  const db = await openDatabase();
  if (!db) return memory[0] ?? null;
  return transact<Entry | null>(db, "readonly", null, (store, done) => {
    const request = store.openCursor();
    request.onsuccess = () => {
      const cursor = request.result;
      if (cursor) done({ key: cursor.key as number, value: cursor.value });
    };
  });
}

async function remove(key: number) {
  const db = await openDatabase();
  if (db)
    await transact(db, "readwrite", undefined, (store) => store.delete(key));
  else {
    const index = memory.findIndex((entry) => entry.key === key);
    if (index >= 0) memory.splice(index, 1);
  }
}

async function all(): Promise<Outgoing[]> {
  const db = await openDatabase();
  if (!db) return memory.map((entry) => entry.value);
  return transact<Outgoing[]>(db, "readonly", [], (store, done) => {
    const request = store.getAll();
    request.onsuccess = () => done(request.result);
  });
}

async function refresh() {
  const items = await all().catch(() => []);
  queued = items.length;
  update({ pending: items.filter((item) => item.kind === "chunk").length });
}

function send(item: Outgoing) {
  if (item.kind === "stream")
    return api.put(`/sessions/${item.sessionId}/streams/${item.streamId}`, {
      closed: true,
      stop: item.stop,
    });
  const form = new FormData();
  for (const key of [
    "sessionId",
    "sourceId",
    "streamId",
    "sequence",
    "startMs",
    "endMs",
    "sha256",
    "recordedAt",
  ] as const)
    form.append(key, String(item[key]));
  form.append("file", item.wav, "capture.wav");
  return api.upload("/chunks", form);
}

// Waits before the next attempt; coming back online or new audio ends the wait early.
function backoff(ms: number) {
  return new Promise<void>((resolve) => {
    const done = () => {
      clearTimeout(timer);
      window.removeEventListener("online", done);
      wake = null;
      resolve();
    };
    const timer = setTimeout(done, ms);
    window.addEventListener("online", done);
    wake = done;
  });
}

async function sendAll() {
  let failures = 0;
  for (let next = await first(); next; next = await first()) {
    try {
      await send(next.value);
      await remove(next.key);
      failures = 0;
      update({ error: null });
      for (const listener of uploadedListeners) listener(next.value.sessionId);
    } catch (reason) {
      const message = reason instanceof Error ? reason.message : String(reason);
      if (reason instanceof ApiError && REJECTED.has(reason.status)) {
        await remove(next.key);
        showError(`The API did not accept recorded audio: ${message}`);
      } else {
        update({ error: message });
        await backoff(RETRY_MS[Math.min(failures++, RETRY_MS.length - 1)]);
      }
    }
    await refresh();
  }
}

// One tab sends at a time; the others' audio is in the same database, so that tab sends it too.
async function drain() {
  if (draining) return;
  draining = true;
  try {
    if (navigator.locks)
      await navigator.locks.request(LOCK, { ifAvailable: true }, (lock) =>
        lock ? sendAll() : undefined,
      );
    else await sendAll();
  } catch (reason) {
    update({
      error: reason instanceof Error ? reason.message : String(reason),
    });
  } finally {
    draining = false;
    await refresh();
  }
}

export function sendUploads() {
  if (wake) wake();
  else void drain();
}

export async function enqueueUpload(item: Outgoing) {
  await add(item);
  await refresh();
  sendUploads();
}

// Where the session's timeline ends counting audio not yet sent, so a new recording starts after it.
export async function pendingEndMs(sessionId: string) {
  let end = 0;
  for (const item of await all())
    if (item.kind === "chunk" && item.sessionId === sessionId)
      end = Math.max(end, item.endMs);
  return end;
}

export function onUploaded(listener: (sessionId: string) => void) {
  uploadedListeners.add(listener);
  return () => void uploadedListeners.delete(listener);
}

export function useUploads() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => void listeners.delete(listener);
    },
    () => state,
  );
}

// Audio left from an earlier visit is sent as soon as the page loads.
if (typeof window !== "undefined") {
  void refresh().then(sendUploads);
  window.addEventListener("online", sendUploads);
  setInterval(() => {
    if (queued > 0) sendUploads();
  }, RETRY_QUEUED_MS);
}
