import { createHash } from "node:crypto";
import {
  closeSync,
  existsSync,
  fsyncSync,
  mkdirSync,
  openSync,
  readFileSync,
  readdirSync,
  renameSync,
  writeFileSync,
} from "node:fs";
import path from "node:path";

export type Pending = {
  id: string;
  sessionId: string;
  sourceId: string;
  sequence: number;
  startMs: number;
  endMs: number;
  hash: string;
  file: string;
  status: string;
  consent: boolean;
  error?: string;
};

export class DurableQueue {
  readonly pending = new Map<string, Pending>();
  readonly sequence = new Map<string, number>();
  error = "";
  constructor(readonly root: string) {
    mkdirSync(root, { recursive: true });
    this.recover();
  }
  private manifest(item: Pending) {
    return path.join(this.root, item.id + ".json");
  }
  update(item: Pending) {
    const temporary = this.manifest(item) + ".tmp";
    this.writeDurable(temporary, JSON.stringify(item));
    renameSync(temporary, this.manifest(item));
    if (item.status === "transcribed") this.pending.delete(item.id);
    else this.pending.set(item.id, item);
  }
  private writeDurable(file: string, bytes: Buffer | string) {
    const handle = openSync(file, "w");
    try {
      writeFileSync(handle, bytes);
      fsyncSync(handle);
    } finally {
      closeSync(handle);
    }
  }
  private recover() {
    for (const name of readdirSync(this.root)) {
      if (!name.endsWith(".json")) continue;
      try {
        const item = JSON.parse(
          readFileSync(path.join(this.root, name), "utf8"),
        ) as Pending;
        if (!existsSync(item.file)) {
          this.error = `Queue audio missing for ${name}`;
          continue;
        }
        if (
          createHash("sha256").update(readFileSync(item.file)).digest("hex") !==
          item.hash
        ) {
          this.error = `Queue audio hash mismatch for ${name}`;
          continue;
        }
        if (item.status !== "transcribed") this.pending.set(item.id, item);
        const key = `${item.sessionId}:${item.sourceId}`;
        this.sequence.set(
          key,
          Math.max(this.sequence.get(key) || 0, item.sequence + 1),
        );
      } catch {
        this.error = `Queue record ${name} needs manual review`;
      }
    }
  }
  save(
    sessionId: string,
    sourceId: string,
    startMs: number,
    endMs: number,
    samples: number[],
    sampleRate: number,
    consent: boolean,
  ): Pending {
    if (
      !/^[a-f0-9]{32}$/.test(sessionId) ||
      !["system", "microphone"].includes(sourceId) ||
      endMs <= startMs ||
      samples.length > 2_000_000 ||
      sampleRate < 1000 ||
      sampleRate > 192000
    )
      throw new Error("Invalid capture chunk");
    const key = `${sessionId}:${sourceId}`;
    const next = this.sequence.get(key) || 0;
    const bytes = Buffer.alloc(44 + samples.length * 2);
    bytes.write("RIFF", 0);
    bytes.writeUInt32LE(bytes.length - 8, 4);
    bytes.write("WAVEfmt ", 8);
    bytes.writeUInt32LE(16, 16);
    bytes.writeUInt16LE(1, 20);
    bytes.writeUInt16LE(1, 22);
    bytes.writeUInt32LE(sampleRate, 24);
    bytes.writeUInt32LE(sampleRate * 2, 28);
    bytes.writeUInt16LE(2, 32);
    bytes.writeUInt16LE(16, 34);
    bytes.write("data", 36);
    bytes.writeUInt32LE(samples.length * 2, 40);
    samples.forEach((sample, index) =>
      bytes.writeInt16LE(
        Math.round(Math.max(-1, Math.min(1, sample)) * 32767),
        44 + index * 2,
      ),
    );
    const hash = createHash("sha256").update(bytes).digest("hex");
    const id = `${sessionId}-${sourceId}-${next}-${hash}`;
    const file = path.join(this.root, id + ".wav");
    const temporary = file + ".tmp";
    this.writeDurable(temporary, bytes);
    renameSync(temporary, file);
    const item: Pending = {
      id,
      sessionId,
      sourceId,
      sequence: next,
      startMs,
      endMs,
      hash,
      file,
      status: "pending-upload",
      consent,
    };
    this.update(item);
    this.sequence.set(key, next + 1);
    return item;
  }
}
