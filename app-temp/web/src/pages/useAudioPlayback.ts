import { useEffect, useRef, useState } from "react";
import type { Chunk, Session } from "../types/api";
import { clockTime } from "./format";

type PlayableChunk = Pick<Chunk, "id" | "startMs" | "endMs" | "recordedAt">;
type Part = {
  url: string;
  startMs: number;
  fromMs: number;
  toMs: number;
  wallOriginMs: number;
};
type Queue = {
  key: string;
  kind: "session" | "row";
  parts: Part[];
  index: number;
  pendingSeekMs: number | null;
};
export type PlayerSnapshot = {
  kind: "session" | "row";
  active: boolean;
  playing: boolean;
  positionMs: number;
  minimumMs: number;
  maximumMs: number;
  currentTime: string;
  endTime: string;
};

function sessionParts(session: Session, source: string): Part[] {
  const windows = new Map<number, Chunk[]>();
  for (const chunk of session.chunks) {
    if (chunk.status === "silent" || (source !== "mix" && chunk.sourceId !== source)) continue;
    const first = Math.floor(chunk.startMs / 30_000);
    const last = Math.floor((chunk.endMs - 1) / 30_000);
    for (let index = first; index <= last; index++) {
      if (!windows.has(index)) windows.set(index, []);
      windows.get(index)!.push(chunk);
    }
  }
  return [...windows.entries()].sort((a, b) => a[0] - b[0]).map(([index, chunks]) => {
    const startMs = index * 30_000;
    const anchor = chunks.find((chunk) => chunk.recordedAt) || chunks[0];
    return {
      url: `/api/sessions/${session.id}/audio/segments/${index}${source === "mix" ? "" : `?source=${encodeURIComponent(source)}`}`,
      startMs,
      fromMs: Math.max(0, Math.min(...chunks.map((chunk) => chunk.startMs)) - startMs),
      toMs: Math.min(30_000, Math.max(...chunks.map((chunk) => chunk.endMs)) - startMs),
      wallOriginMs: anchor.recordedAt
        ? new Date(anchor.recordedAt).getTime() + startMs - anchor.startMs
        : new Date(session.createdAt).getTime() + startMs,
    };
  });
}

function rowParts(session: Session, chunks: PlayableChunk[]): Part[] {
  return [...chunks].sort((a, b) => a.startMs - b.startMs).map((chunk) => ({
    url: `/api/chunks/${chunk.id}/audio`,
    startMs: chunk.startMs,
    fromMs: 0,
    toMs: chunk.endMs - chunk.startMs,
    wallOriginMs: chunk.recordedAt
      ? new Date(chunk.recordedAt).getTime()
      : new Date(session.createdAt).getTime() + chunk.startMs,
  }));
}

function partAt(parts: Part[], positionMs: number) {
  const index = parts.findIndex((part) => positionMs < part.startMs + part.toMs);
  const selected = index < 0 ? parts.length - 1 : index;
  const part = parts[selected];
  return {
    index: selected,
    offsetMs: Math.max(part.fromMs, Math.min(part.toMs, positionMs - part.startMs)),
  };
}

export function useAudioPlayback(session: Session | null, setError: (message: string) => void) {
  const audio = useRef<HTMLAudioElement>(null);
  const queue = useRef<Queue | null>(null);
  const latestSession = useRef(session);
  latestSession.current = session;
  const [playingKey, setPlayingKey] = useState<string | null>(null);
  const [sourceMode, setSourceModeState] = useState("mix");
  const sourceModeRef = useRef("mix");
  const [speed, setSpeedState] = useState(1);
  const speedRef = useRef(1);
  const partsCache = useRef<{ id: string; createdAt: string; chunks: Session["chunks"]; modes: Map<string, Part[]> } | undefined>(undefined);
  function cachedSessionParts(current: Session, source: string) {
    if (partsCache.current?.id !== current.id || partsCache.current.chunks !== current.chunks || partsCache.current.createdAt !== current.createdAt)
      partsCache.current = { id: current.id, createdAt: current.createdAt, chunks: current.chunks, modes: new Map() };
    const cache = partsCache.current!;
    if (!cache.modes.has(source)) cache.modes.set(source, sessionParts(current, source));
    return cache.modes.get(source)!;
  }

  useEffect(() => {
    audio.current?.pause();
    audio.current?.removeAttribute("src");
    queue.current = null;
    setPlayingKey(null);
    sourceModeRef.current = "mix";
    setSourceModeState("mix");
  }, [session?.id]);

  function playCurrent(selected: Queue) {
    const player = audio.current;
    if (!player) return;
    player.play().then(() => {
      if (queue.current === selected) setPlayingKey(selected.key);
    }).catch((error) => {
      if (queue.current === selected) {
        setPlayingKey(null);
        setError(`Audio playback failed: ${error.message}`);
      }
    });
  }

  function loadPart(selected: Queue, index: number, offsetMs: number, play: boolean) {
    const player = audio.current;
    if (!player) return;
    selected.index = index;
    selected.pendingSeekMs = offsetMs;
    player.pause();
    player.src = selected.parts[index].url;
    player.playbackRate = speedRef.current;
    player.load();
    if (play) playCurrent(selected);
    else setPlayingKey(null);
  }

  function startQueue(key: string, kind: Queue["kind"], parts: Part[], positionMs?: number, play = true) {
    if (!parts.length) return;
    const selected: Queue = { key, kind, parts, index: 0, pendingSeekMs: null };
    queue.current = selected;
    const position = partAt(parts, positionMs ?? parts[0].startMs + parts[0].fromMs);
    loadPart(selected, position.index, position.offsetMs, play);
  }

  function togglePlayback(key: string, chunks: PlayableChunk[]) {
    const current = queue.current;
    if (current?.kind === "row" && current.key === key) {
      toggleCurrentPlayback();
      return;
    }
    const currentSession = latestSession.current;
    if (currentSession) startQueue(key, "row", rowParts(currentSession, chunks));
  }

  function startSessionPlayback(positionMs?: number, play = true) {
    const currentSession = latestSession.current;
    if (!currentSession) return;
    startQueue("session", "session", cachedSessionParts(currentSession, sourceModeRef.current), positionMs, play);
  }

  function toggleCurrentPlayback() {
    const selected = queue.current;
    const player = audio.current;
    if (!selected || !player) {
      startSessionPlayback();
      return;
    }
    if (player.paused) playCurrent(selected);
    else {
      player.pause();
      setPlayingKey(null);
    }
  }

  function seekPlayback(positionMs: number) {
    const selected = queue.current;
    const player = audio.current;
    if (!selected || !player) {
      startSessionPlayback(positionMs);
      return;
    }
    const target = partAt(selected.parts, positionMs);
    if (target.index === selected.index && player.readyState >= HTMLMediaElement.HAVE_METADATA) {
      player.currentTime = Math.min(player.duration || Infinity, target.offsetMs / 1000);
      return;
    }
    loadPart(selected, target.index, target.offsetMs, !player.paused);
  }

  function skipPlayback(milliseconds: number) {
    seekPlayback(getPlaybackSnapshot().positionMs + milliseconds);
  }

  function chooseSourceMode(next: string) {
    if (next === sourceModeRef.current) return;
    const position = getPlaybackSnapshot().positionMs;
    const wasPlaying = !!audio.current && !audio.current.paused;
    sourceModeRef.current = next;
    setSourceModeState(next);
    if (queue.current?.kind === "session") startSessionPlayback(position, wasPlaying);
  }

  function chooseSpeed(next: number) {
    speedRef.current = next;
    setSpeedState(next);
    if (audio.current) audio.current.playbackRate = next;
  }

  function audioLoaded() {
    const selected = queue.current;
    const player = audio.current;
    if (!selected || !player || selected.pendingSeekMs === null) return;
    const seekSeconds = selected.pendingSeekMs / 1000;
    player.currentTime = Number.isFinite(player.duration)
      ? Math.min(Math.max(0, player.duration - 0.01), seekSeconds)
      : seekSeconds;
    selected.pendingSeekMs = null;
  }

  function audioEnded() {
    const selected = queue.current;
    if (!selected) return;
    if (selected.index + 1 < selected.parts.length) {
      loadPart(selected, selected.index + 1, selected.parts[selected.index + 1].fromMs, true);
      return;
    }
    if (selected.kind === "session" && latestSession.current) {
      const currentEnd = selected.parts.at(-1)!.startMs;
      const more = cachedSessionParts(latestSession.current, sourceModeRef.current)
        .filter((part) => part.startMs > currentEnd);
      if (more.length) {
        selected.parts.push(...more);
        loadPart(selected, selected.index + 1, selected.parts[selected.index + 1].fromMs, true);
        return;
      }
    }
    queue.current = null;
    setPlayingKey(null);
  }

  function getPlaybackSnapshot(): PlayerSnapshot {
    const selected = queue.current;
    const player = audio.current;
    const parts = selected?.parts || (latestSession.current ? cachedSessionParts(latestSession.current, sourceModeRef.current) : []);
    const first = parts[0];
    const last = parts.at(-1);
    const current = selected && player ? parts[selected.index] : first;
    const currentOffset = selected && player && Number.isFinite(player.currentTime)
      ? player.currentTime * 1000
      : current?.fromMs || 0;
    const positionMs = current ? Math.min(current.startMs + current.toMs, current.startMs + currentOffset) : 0;
    return {
      kind: selected?.kind || "session",
      active: !!selected,
      playing: !!selected && !!player && !player.paused,
      positionMs,
      minimumMs: first ? first.startMs + first.fromMs : 0,
      maximumMs: last ? last.startMs + last.toMs : 0,
      currentTime: current ? clockTime(new Date(current.wallOriginMs + currentOffset)) : "--:--:--",
      endTime: last ? clockTime(new Date(last.wallOriginMs + last.toMs)) : "--:--:--",
    };
  }

  return {
    audio,
    playingKey,
    sourceMode,
    speed,
    togglePlayback,
    startSessionPlayback,
    toggleCurrentPlayback,
    seekPlayback,
    skipPlayback,
    chooseSourceMode,
    chooseSpeed,
    audioLoaded,
    audioEnded,
    getPlaybackSnapshot,
  };
}
