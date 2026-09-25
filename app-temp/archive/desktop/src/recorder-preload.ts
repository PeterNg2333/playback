import { contextBridge, ipcRenderer } from "electron";
contextBridge.exposeInMainWorld("nativeQueue", {
  save: (
    sourceId: string,
    startMs: number,
    endMs: number,
    samples: Float32Array,
    sampleRate: number,
  ) =>
    ipcRenderer.invoke("capture:chunk", {
      sourceId,
      startMs,
      endMs,
      samples: Array.from(samples),
      sampleRate,
    }),
  error: (message: string) => ipcRenderer.send("capture:error", message),
});
