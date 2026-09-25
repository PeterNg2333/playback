import { contextBridge, ipcRenderer } from "electron";
contextBridge.exposeInMainWorld("playbackCapture", {
  status: () => ipcRenderer.invoke("capture:status"),
  start: (sessionId: string) => ipcRenderer.invoke("capture:start", sessionId),
  pause: () => ipcRenderer.invoke("capture:pause"),
  resume: () => ipcRenderer.invoke("capture:resume"),
  stop: () => ipcRenderer.invoke("capture:stop"),
});
