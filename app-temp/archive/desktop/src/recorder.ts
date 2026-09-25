type NativeQueue = {
  save: (
    sourceId: string,
    startMs: number,
    endMs: number,
    samples: Float32Array,
    sampleRate: number,
  ) => Promise<void>;
  error: (message: string) => void;
};
interface Window {
  nativeQueue: NativeQueue;
  recorder: {
    start: () => Promise<void>;
    pause: () => void;
    resume: () => void;
    stop: () => Promise<void>;
  };
}
type Source = {
  stream: MediaStream;
  context: AudioContext;
  processor: ScriptProcessorNode;
  start: number;
  samples: Float32Array[];
  count: number;
  silent: number;
};
const active = new Map<string, Source>();
const pendingSaves = new Set<Promise<void>>();
let recordingStart = 0;
let paused = false;

async function flush(name: string, end = Date.now()) {
  const source = active.get(name);
  if (!source || !source.count) return;
  const samples = new Float32Array(source.count);
  let offset = 0;
  for (const block of source.samples) {
    samples.set(block, offset);
    offset += block.length;
  }
  const startMs = source.start - recordingStart,
    endMs = end - recordingStart;
  source.samples = [];
  source.count = 0;
  source.start = end;
  const saved = window.nativeQueue.save(
    name,
    startMs,
    endMs,
    samples,
    source.context.sampleRate,
  );
  pendingSaves.add(saved);
  try { await saved } finally { pendingSaves.delete(saved) }
}
function connect(name: string, stream: MediaStream) {
  const audio = stream.getAudioTracks()[0];
  if (!audio) {
    window.nativeQueue.error(`${name}: no audio track was granted`);
    stream.getTracks().forEach((track) => track.stop());
    return;
  }
  const context = new AudioContext();
  const sourceNode = context.createMediaStreamSource(new MediaStream([audio]));
  const processor = context.createScriptProcessor(4096, 1, 1);
  const silence = context.createGain();
  silence.gain.value = 0;
  sourceNode.connect(processor);
  processor.connect(silence);
  silence.connect(context.destination);
  const entry: Source = {
    stream,
    context,
    processor,
    start: Date.now(),
    samples: [],
    count: 0,
    silent: 0,
  };
  active.set(name, entry);
  processor.onaudioprocess = (event) => {
    if (paused) return;
    const block = new Float32Array(event.inputBuffer.getChannelData(0));
    entry.samples.push(block);
    entry.count += block.length;
    let energy = 0;
    for (const value of block) energy += value * value;
    entry.silent = energy / block.length < 0.000001 ? entry.silent + 1 : 0;
    if (entry.silent === 120)
      window.nativeQueue.error(
        `${name}: captured stream appears silent; check source and permissions`,
      );
    if (entry.count >= context.sampleRate * 10)
      void flush(name).catch((error) =>
        window.nativeQueue.error(String(error)),
      );
  };
  audio.onended = () => {
    window.nativeQueue.error(`${name}: capture stream ended`);
    void flush(name);
  };
}
window.recorder = {
  async start() {
    if (active.size) throw new Error("Capture already active");
    recordingStart = Date.now();
    paused = false;
    if (navigator.userAgent.includes("Windows")) {
      try {
        connect(
          "system",
          await navigator.mediaDevices.getDisplayMedia({
            video: true,
            audio: true,
          }),
        );
      } catch (error) {
        window.nativeQueue.error(
          `System audio capture failed: ${String(error)}`,
        );
      }
    } else
      window.nativeQueue.error(
        "Whole-system capture has only been configured for Windows",
      );
    try {
      connect(
        "microphone",
        await navigator.mediaDevices.getUserMedia({ audio: true }),
      );
    } catch (error) {
      window.nativeQueue.error(
        `Microphone permission or device failed: ${String(error)}`,
      );
    }
    if (!active.size) throw new Error("No audio source started");
  },
  pause() {
    paused = true;
    for (const name of active.keys()) void flush(name);
  },
  resume() {
    paused = false;
    for (const source of active.values()) source.start = Date.now();
  },
  async stop() {
    paused = true;
    await Promise.all([...active.keys()].map((name) => flush(name)));
    await Promise.all(pendingSaves);
    for (const source of active.values()) {
      source.processor.disconnect();
      source.stream.getTracks().forEach((track) => track.stop());
      await source.context.close();
    }
    active.clear();
  },
};
