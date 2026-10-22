import { api } from "../../lib/backend/client";
import type {
  CaptureStatus,
  RecordingMode,
  Session,
} from "../../lib/backend/schemas";
import { showError } from "../../lib/store";
import { enqueueUpload, onUploaded, pendingEndMs } from "./uploadQueue";
import { sha256Hex, wavFile } from "./wav";

// Records in this tab: the microphone through getUserMedia, system audio through screen sharing with audio. Each source
// is kept as 16-bit mono WAV chunks (16 kHz where the browser can resample, otherwise its own rate) and queued for upload.
// Chunks close like the Windows recorder's: after 8 s, or after 3 s once speech was heard and every source has been quiet
// for 0.7 s. Speech here is a level rule for chunk boundaries and the live row only; the API's VAD still decides what
// reaches ASR, and every chunk is kept. Pause keeps the devices open, so resuming does not ask to share the screen again.
const CHUNK_MS = 8_000;
const MIN_CHUNK_BEFORE_PAUSE_MS = 3_000;
const PAUSE_MS = 700;
const PUMP_MS = 250;
const RENEW_MS = 20_000;
const NO_SOUND_WARNING_MS = 60_000;
const SAMPLE_RATE = 16_000;
// A block is speech when it is this far above the source's noise floor and above an absolute minimum.
const SPEECH_ABOVE_FLOOR_DB = 10;
const SPEECH_MIN_DB = -50;
const FLOOR_RISE_DB_PER_S = 0.5;

// Mixes each input block to mono and posts about 2,048 frames at a time.
const WORKLET = `
class PlaybackPcmTap extends AudioWorkletProcessor {
  constructor() { super(); this.block = new Float32Array(2048); this.length = 0; }
  process(inputs) {
    const input = inputs[0];
    if (input && input.length) {
      for (let frame = 0; frame < input[0].length; frame++) {
        let sum = 0;
        for (const channel of input) sum += channel[frame];
        this.block[this.length++] = sum / input.length;
        if (this.length === this.block.length) {
          this.port.postMessage(this.block, [this.block.buffer]);
          this.block = new Float32Array(2048);
          this.length = 0;
        }
      }
    }
    return true;
  }
}
registerProcessor("playback-pcm-tap", PlaybackPcmTap);
`;

type SourceId = "microphone" | "system";

type Source = {
  id: SourceId;
  stream: MediaStream;
  input: MediaStreamAudioSourceNode;
  node: AudioWorkletNode;
  blocks: Float32Array[];
  frames: number;
  bytes: number;
  level: number;
  floorDb: number;
  heard: boolean;
  voicedMs: number;
  lastSpeechMs: number;
};

type Recording = {
  id: string;
  sessionId: string;
  sourceMode: RecordingMode;
  streamId: string;
  context: AudioContext;
  sources: Source[];
  offsetMs: number;
  frames: number;
  chunkStartMs: number;
  chunkStartedAt: number;
  paused: boolean;
  lastSoundAt: number;
  uploadedAtMs: number;
  error: string | null;
  timers: number[];
  finalizing: Promise<void>;
  unsubscribe: () => void;
};

// What this browser can record; system audio needs screen sharing, which phones do not offer.
export function browserRecording() {
  const devices =
    typeof navigator === "undefined" ? undefined : navigator.mediaDevices;
  const audio =
    typeof window !== "undefined" &&
    window.isSecureContext &&
    typeof AudioWorkletNode !== "undefined" &&
    !!devices?.getUserMedia;
  return {
    microphone: audio,
    systemAudio: audio && !!devices?.getDisplayMedia,
  };
}

let workletUrl: string | undefined;
let lastSequence = 0;
// Where this tab's last chunk for each session ended: the session read may not include it yet.
const recordedEnds = new Map<string, number>();

// Unique per source within a session, like the Windows recorder's tick sequence; microseconds stay a safe integer.
function nextSequence() {
  lastSequence = Math.max(Date.now() * 1000, lastSequence + 1);
  return lastSequence;
}

function sourceLabel(id: SourceId) {
  return id === "system" ? "System audio" : "Microphone";
}

function describe(error: unknown) {
  if (error instanceof DOMException && error.name === "NotAllowedError")
    return "permission was denied or sharing was cancelled";
  if (error instanceof DOMException && error.name === "NotFoundError")
    return "no device was found";
  return error instanceof Error ? error.message : String(error);
}

async function shareSystemAudio() {
  const stream = await navigator.mediaDevices.getDisplayMedia({
    video: { frameRate: { ideal: 1 } },
    audio: {
      echoCancellation: false,
      noiseSuppression: false,
      autoGainControl: false,
    },
    // Chrome and Edge: offer “Also share system audio” for a whole screen, and leave this tab out.
    systemAudio: "include",
    selfBrowserSurface: "exclude",
  } as DisplayMediaStreamOptions);
  if (stream.getAudioTracks().length) return stream;
  for (const track of stream.getTracks()) track.stop();
  throw new Error(
    "no audio was shared. Choose Entire screen and turn on “Also share system audio”, or choose a tab and turn on “Also share tab audio”",
  );
}

async function openStreams(mode: RecordingMode) {
  // Screen sharing first: it needs the click that started recording.
  const wanted: SourceId[] =
    mode === "both" ? ["system", "microphone"] : [mode];
  const opened: { id: SourceId; stream: MediaStream }[] = [];
  const failures: string[] = [];
  for (const id of wanted)
    try {
      opened.push({
        id,
        stream:
          id === "system"
            ? await shareSystemAudio()
            : await navigator.mediaDevices.getUserMedia({ audio: true }),
      });
    } catch (error) {
      failures.push(`${sourceLabel(id)}: ${describe(error)}`);
    }
  if (!opened.length)
    throw new Error(`No audio source could start. ${failures.join("; ")}`);
  return { opened, error: failures.length ? failures.join("; ") : null };
}

async function audioContext(sampleRate?: number) {
  const context = new AudioContext(sampleRate ? { sampleRate } : undefined);
  workletUrl ??= URL.createObjectURL(
    new Blob([WORKLET], { type: "text/javascript" }),
  );
  await context.audioWorklet.addModule(workletUrl);
  return context;
}

// 16 kHz when the browser resamples microphone and shared audio into it; otherwise the device rate.
async function connect(opened: { id: SourceId; stream: MediaStream }[]) {
  for (const rate of [SAMPLE_RATE, undefined]) {
    let context: AudioContext | undefined;
    try {
      context = await audioContext(rate);
      const sink = context.createGain();
      sink.gain.value = 0;
      sink.connect(context.destination);
      const sources = opened.map(({ id, stream }): Source => {
        const input = context!.createMediaStreamSource(stream);
        const node = new AudioWorkletNode(context!, "playback-pcm-tap");
        input.connect(node).connect(sink);
        return {
          id,
          stream,
          input,
          node,
          blocks: [],
          frames: 0,
          bytes: 0,
          level: 0,
          floorDb: -60,
          heard: false,
          voicedMs: 0,
          lastSpeechMs: -Infinity,
        };
      });
      await context.resume();
      return { context, sources };
    } catch (error) {
      await context?.close().catch(() => undefined);
      if (rate === undefined) throw error;
    }
  }
  throw new Error("Audio processing is unavailable in this browser");
}

function release(source: Source) {
  source.node.port.onmessage = null;
  source.node.disconnect();
  source.input.disconnect();
  for (const track of source.stream.getTracks()) track.stop();
}

function clockMs(recording: Recording) {
  return (
    recording.offsetMs +
    Math.round((recording.frames / recording.context.sampleRate) * 1000)
  );
}

function warnBeforeLeaving(event: BeforeUnloadEvent) {
  event.preventDefault();
}

class BrowserRecorder {
  private current: Recording | null = null;

  get active() {
    return this.current !== null;
  }

  status(): CaptureStatus | null {
    const recording = this.current;
    if (!recording) return null;
    const end = clockMs(recording);
    const speaking = (source: Source) =>
      source.voicedMs >= 1_000 && end - source.lastSpeechMs < PAUSE_MS;
    return {
      state: recording.paused ? "paused" : "recording",
      sessionId: recording.sessionId,
      sourceMode: recording.sourceMode,
      bytes: Object.fromEntries(recording.sources.map((s) => [s.id, s.bytes])),
      autoAsr: true,
      noSoundWarning:
        !recording.paused &&
        Date.now() - recording.lastSoundAt > NO_SOUND_WARNING_MS,
      error: recording.error,
      levels: Object.fromEntries(
        recording.sources.map((s) => [s.id, recording.paused ? 0 : s.level]),
      ),
      capturedThroughMs: end,
      lastFinalizedAtMs: recording.uploadedAtMs,
      recordingElapsedMs: end - recording.offsetMs,
      recordingId: recording.id,
      chunkMilliseconds: CHUNK_MS,
      activeSegments: recording.paused
        ? []
        : recording.sources
            .filter((s) => s.heard && end > recording.chunkStartMs)
            .map((s) => ({
              sourceId: s.id,
              startMs: recording.chunkStartMs,
              endMs: end,
              recordedAt: new Date(recording.chunkStartedAt).toISOString(),
              streaming: speaking(s),
            })),
    };
  }

  async start(session: Session, mode: RecordingMode) {
    if (this.current) throw new Error("Recording is already active");
    const support = browserRecording();
    if (!support.microphone)
      throw new Error(
        "This browser cannot record here. Open Playback over HTTPS in a current Chrome, Edge, Firefox or Safari",
      );
    if (mode !== "microphone" && !support.systemAudio)
      throw new Error(
        "This browser cannot share system audio; choose Microphone",
      );
    const { opened, error } = await openStreams(mode);
    let graph: Awaited<ReturnType<typeof connect>>;
    try {
      graph = await connect(opened);
    } catch (reason) {
      for (const { stream } of opened)
        for (const track of stream.getTracks()) track.stop();
      throw reason;
    }
    const offsetMs = Math.max(
      0,
      ...session.chunks.map((chunk) => chunk.endMs),
      recordedEnds.get(session.id) ?? 0,
      await pendingEndMs(session.id).catch(() => 0),
    );
    const recording: Recording = {
      id: crypto.randomUUID().replaceAll("-", ""),
      sessionId: session.id,
      sourceMode: mode,
      streamId: crypto.randomUUID().replaceAll("-", ""),
      context: graph.context,
      sources: graph.sources,
      offsetMs,
      frames: 0,
      chunkStartMs: offsetMs,
      chunkStartedAt: Date.now(),
      paused: false,
      lastSoundAt: Date.now(),
      uploadedAtMs: 0,
      error,
      timers: [],
      finalizing: Promise.resolve(),
      unsubscribe: onUploaded((id) => {
        if (id === recording.sessionId) recording.uploadedAtMs = Date.now();
      }),
    };
    for (const source of recording.sources) {
      source.node.port.onmessage = (event: MessageEvent<Float32Array>) =>
        this.receive(recording, source, event.data);
      for (const track of source.stream.getTracks())
        track.addEventListener("ended", () => this.ended(recording, source));
    }
    this.current = recording;
    this.run(recording);
    window.addEventListener("beforeunload", warnBeforeLeaving);
    // Ask the browser not to evict audio that is still waiting to upload.
    void navigator.storage?.persist?.().catch(() => undefined);
  }

  async pause() {
    const recording = this.current;
    if (!recording || recording.paused) return;
    recording.paused = true;
    this.halt(recording);
    await this.finalize(recording);
    await enqueueUpload({
      kind: "stream",
      sessionId: recording.sessionId,
      streamId: recording.streamId,
      stop: false,
    });
  }

  async resume() {
    const recording = this.current;
    if (!recording?.paused) return;
    recording.streamId = crypto.randomUUID().replaceAll("-", "");
    recording.chunkStartMs = clockMs(recording);
    recording.chunkStartedAt = Date.now();
    recording.lastSoundAt = Date.now();
    recording.paused = false;
    this.run(recording);
  }

  async stop() {
    const recording = this.current;
    if (!recording) return;
    this.halt(recording);
    try {
      if (!recording.paused) {
        recording.paused = true;
        await this.finalize(recording);
      }
      // A paused stream is already closed; closing it again with Stop asks for the after-lecture notes.
      await enqueueUpload({
        kind: "stream",
        sessionId: recording.sessionId,
        streamId: recording.streamId,
        stop: true,
      });
    } finally {
      for (const source of recording.sources) release(source);
      void recording.context.close().catch(() => undefined);
      recording.unsubscribe();
      window.removeEventListener("beforeunload", warnBeforeLeaving);
      this.current = null;
    }
  }

  // Seals and renews while recording: one timer checks chunk boundaries, one keeps the stream's lease.
  private run(recording: Recording) {
    const renew = () =>
      void api
        .put(`/sessions/${recording.sessionId}/streams/${recording.streamId}`, {
          closed: false,
        })
        .catch(() => undefined);
    renew();
    recording.timers = [
      window.setInterval(() => this.pump(recording), PUMP_MS),
      window.setInterval(renew, RENEW_MS),
    ];
  }

  private halt(recording: Recording) {
    for (const timer of recording.timers) clearInterval(timer);
    recording.timers = [];
  }

  private receive(recording: Recording, source: Source, block: Float32Array) {
    if (recording.paused) return;
    const rate = recording.context.sampleRate;
    const blockMs = (block.length / rate) * 1000;
    let peak = 0,
      energy = 0;
    for (const sample of block) {
      peak = Math.max(peak, Math.abs(sample));
      energy += sample * sample;
    }
    const db =
      20 * Math.log10(Math.max(Math.sqrt(energy / block.length), 1e-5));
    source.floorDb =
      db < source.floorDb
        ? db
        : source.floorDb + (FLOOR_RISE_DB_PER_S * blockMs) / 1000;
    source.level = Math.round(Math.min(1, peak) * 1000);
    source.frames += block.length;
    source.bytes += block.length * 2;
    source.blocks.push(block);
    recording.frames = Math.max(recording.frames, source.frames);
    if (db > Math.max(SPEECH_MIN_DB, source.floorDb + SPEECH_ABOVE_FLOOR_DB)) {
      source.heard = true;
      source.voicedMs += blockMs;
      source.lastSpeechMs = clockMs(recording);
      recording.lastSoundAt = Date.now();
    }
  }

  private pump(recording: Recording) {
    if (recording.paused) return;
    const end = clockMs(recording);
    const elapsed = end - recording.chunkStartMs;
    if (
      elapsed >= CHUNK_MS ||
      (elapsed >= MIN_CHUNK_BEFORE_PAUSE_MS &&
        recording.sources.some((s) => s.heard) &&
        recording.sources.every((s) => end - s.lastSpeechMs >= PAUSE_MS))
    )
      void this.finalize(recording);
  }

  // Cuts every source at the same time and queues one WAV per source; queued in order, after earlier chunks.
  private finalize(recording: Recording) {
    const startMs = recording.chunkStartMs;
    const endMs = clockMs(recording);
    if (endMs <= startMs) return recording.finalizing;
    const recordedAt = new Date(recording.chunkStartedAt).toISOString();
    const parts = recording.sources
      .filter((source) => source.blocks.length)
      .map((source) => {
        const part = { sourceId: source.id, blocks: source.blocks };
        source.blocks = [];
        source.heard = false;
        source.voicedMs = 0;
        return part;
      });
    recording.chunkStartMs = endMs;
    recording.chunkStartedAt = Date.now();
    recordedEnds.set(recording.sessionId, endMs);
    const { sessionId, streamId } = recording;
    const rate = recording.context.sampleRate;
    recording.finalizing = recording.finalizing
      .then(async () => {
        for (const part of parts) {
          const wav = wavFile(part.blocks, rate);
          await enqueueUpload({
            kind: "chunk",
            sessionId,
            sourceId: part.sourceId,
            streamId,
            sequence: nextSequence(),
            startMs,
            endMs,
            sha256: await sha256Hex(wav),
            recordedAt,
            wav: new Blob([wav], { type: "audio/wav" }),
          });
        }
      })
      .catch((error) => {
        recording.error = `Recorded audio could not be saved in the browser: ${describe(error)}`;
      });
    return recording.finalizing;
  }

  // The user stopped sharing or a device went away: keep what it recorded, and stop when no source is left.
  private ended(recording: Recording, source: Source) {
    if (this.current !== recording || !recording.sources.includes(source))
      return;
    void this.finalize(recording);
    release(source);
    recording.sources = recording.sources.filter((s) => s !== source);
    const message = `${sourceLabel(source.id)} ended${source.id === "system" ? " because screen sharing stopped" : ""}`;
    if (recording.sources.length) recording.error = message;
    else
      void this.stop().then(() =>
        showError(`${message}; recording stopped and its audio is kept`),
      );
  }
}

export const browserRecorder = new BrowserRecorder();
