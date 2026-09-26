import {
  app,
  BrowserWindow,
  desktopCapturer,
  ipcMain,
  session,
  shell,
} from "electron";
import { readFileSync } from "node:fs";
import path from "node:path";
import { DurableQueue } from "./queue";

const root = path.resolve(__dirname, "..", "..", "data", "desktop");
app.setPath("userData", root);
const api = "http://127.0.0.1:5078";
let ui: BrowserWindow, recorder: BrowserWindow;
let currentSession = "",
  state = "idle",
  error = "",
  currentConsent = false;
let uploadBusy = false;
const queue = new DurableQueue(path.join(root, "chunks"));
error = queue.error;
let closing = false;
async function flush() {
  if (uploadBusy) return;
  uploadBusy = true;
  try {
    for (const item of queue.pending.values()) {
      if (item.status === "pending-asr") continue;
      try {
        const body = new FormData();
        for (const key of [
          "sessionId",
          "sourceId",
          "sequence",
          "startMs",
          "endMs",
        ] as const)
          body.set(key, String(item[key]));
        body.set("sha256", item.hash);
        body.set(
          "file",
          new Blob([readFileSync(item.file)], { type: "audio/wav" }),
          "audio.wav",
        );
        const response = await fetch(`${api}/api/chunks`, {
          method: "POST",
          body,
          headers: item.consent ? { "X-Playback-External-Consent": "yes" } : {},
          signal: AbortSignal.timeout(135000),
        });
        const result = (await response.json()) as {
          status?: string;
          error?: string;
        };
        item.status = response.status === 502 ? "pending-upload" : result.status || "pending-upload";
        item.error = result.error;
        if (!response.ok && response.status !== 502)
          item.error = result.error || `HTTP ${response.status}`;
        if (item.error)
          error = `${item.sourceId} chunk ${item.sequence}: ${item.error}`;
        queue.update(item);
      } catch (caught) {
        item.error = caught instanceof Error ? caught.message : String(caught);
        error = `${item.sourceId} chunk ${item.sequence}: ${item.error}`;
        queue.update(item);
      }
    }
  } finally {
    uploadBusy = false;
  }
}

app.whenReady().then(async () => {
  session.defaultSession.setDisplayMediaRequestHandler(
    async (_request, callback) => {
      const sources = await desktopCapturer.getSources({ types: ["screen"] });
      if (!sources.length) {
        error = "No screen capture source is available";
        return;
      }
      callback({ video: sources[0], audio: "loopback" });
    },
  );
  ui = new BrowserWindow({
    width: 1400,
    height: 900,
    webPreferences: {
      preload: path.join(__dirname, "preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
    },
  });
  recorder = new BrowserWindow({
    show: false,
    webPreferences: {
      preload: path.join(__dirname, "recorder-preload.js"),
      contextIsolation: true,
      nodeIntegration: false,
      backgroundThrottling: false,
    },
  });
  await recorder.loadFile(path.join(__dirname, "..", "recorder.html"));
  await ui.loadURL("http://127.0.0.1:5173");
  ui.webContents.setWindowOpenHandler((details) => {
    if (details.url.startsWith("https://"))
      void shell.openExternal(details.url);
    return { action: "deny" };
  });
  ui.webContents.on("will-navigate", (event, url) => {
    if (!url.startsWith("http://127.0.0.1:5173/")) event.preventDefault();
  });
  ui.on("close", (event) => {
    if (closing) return;
    event.preventDefault();
    closing = true;
    void (async () => {
      if (state !== "idle")
        await recorder.webContents.executeJavaScript("window.recorder.stop()");
      state = "idle";
      currentSession = "";
      recorder.destroy();
      ui.destroy();
      app.quit();
    })().catch((caught) => {
      error = `Stop failed: ${String(caught)}`;
      closing = false;
    });
  });
  ipcMain.handle("capture:status", () => ({
    state,
    pending: queue.pending.size,
    error,
  }));
  ipcMain.handle(
    "capture:start",
    async (_event, sessionId: string, consent: boolean) => {
      if (!/^[a-f0-9]{32}$/.test(sessionId)) throw new Error("Invalid session");
      if (state !== "idle") throw new Error("Capture is already active");
      currentSession = sessionId;
      currentConsent = consent === true;
      try {
        await recorder.webContents.executeJavaScript(
          "window.recorder.start()",
          true,
        );
        state = "recording";
        error = "";
      } catch (caught) {
        currentSession = "";
        error = caught instanceof Error ? caught.message : String(caught);
        throw caught;
      }
    },
  );
  ipcMain.handle("capture:pause", async () => {
    await recorder.webContents.executeJavaScript("window.recorder.pause()");
    state = "paused";
  });
  ipcMain.handle("capture:resume", async () => {
    await recorder.webContents.executeJavaScript("window.recorder.resume()");
    state = "recording";
  });
  ipcMain.handle("capture:stop", async () => {
    await recorder.webContents.executeJavaScript("window.recorder.stop()");
    state = "idle";
    currentSession = "";
    currentConsent = false;
    void flush();
  });
  ipcMain.on("capture:error", (_event, message: string) => {
    error = message;
  });
  ipcMain.handle(
    "capture:chunk",
    async (
      _event,
      input: {
        sourceId: string;
        startMs: number;
        endMs: number;
        samples: number[];
        sampleRate: number;
      },
    ) => {
      queue.save(
        currentSession,
        input.sourceId,
        input.startMs,
        input.endMs,
        input.samples,
        input.sampleRate,
        currentConsent,
      );
      void flush();
    },
  );
  setInterval(() => {
    void flush();
  }, 10_000);
  void flush();
});

app.on("window-all-closed", () => {
  if (state === "idle") app.quit();
});
