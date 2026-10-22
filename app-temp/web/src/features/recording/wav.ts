// A 16-bit PCM mono WAV of the recorded samples, and the SHA-256 the API checks an upload against.
export function wavFile(blocks: Float32Array[], sampleRate: number) {
  const frames = blocks.reduce((total, block) => total + block.length, 0);
  const view = new DataView(new ArrayBuffer(44 + frames * 2));
  const text = (offset: number, value: string) => {
    for (let index = 0; index < value.length; index++)
      view.setUint8(offset + index, value.charCodeAt(index));
  };
  text(0, "RIFF");
  view.setUint32(4, 36 + frames * 2, true);
  text(8, "WAVEfmt ");
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * 2, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  text(36, "data");
  view.setUint32(40, frames * 2, true);
  let offset = 44;
  for (const block of blocks)
    for (const sample of block) {
      const clamped = Math.max(-1, Math.min(1, sample || 0));
      view.setInt16(
        offset,
        clamped < 0 ? clamped * 0x8000 : clamped * 0x7fff,
        true,
      );
      offset += 2;
    }
  return new Uint8Array(view.buffer);
}

export async function sha256Hex(bytes: Uint8Array<ArrayBuffer>) {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", bytes));
  return Array.from(digest, (byte) => byte.toString(16).padStart(2, "0")).join(
    "",
  );
}
