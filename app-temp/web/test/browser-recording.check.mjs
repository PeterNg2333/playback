// Recording in the browser, offline: Edge's fake microphone and a page-made screen share with audio stand in for the
// devices, and fixture routes stand in for the API. Checks each uploaded WAV (identity fields, 16 kHz mono PCM, SHA-256,
// a timeline that continues the session's), retry after a failure, pause/resume/stop stream updates in order, audio kept
// in IndexedDB across a reload, the shared screen ending, a share without audio, and the recorder choice.
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { launchBrowser } from "./browser.mjs";

const url = process.env.PLAYBACK_WEB_TEST_URL ?? "http://127.0.0.1:5174";
assert(/^http:\/\/127\.0\.0\.1:\d+$/.test(url));
const id = "c".repeat(32);
const session = {
  id,
  title: "Browser recording",
  createdAt: "2026-10-22T00:00:00Z",
  groupId: null,
  noteMarkdown: "",
  noteVersion: 0,
  noteLanguage: "en",
  translationEnabled: false,
  translationLanguage: "zh-Hant",
  transcripts: [],
  // An earlier recording ends at 30 s, so new audio starts there.
  chunks: [
    {
      id: "earlier",
      sourceId: "microphone",
      sequence: 1,
      startMs: 22_000,
      endMs: 30_000,
      status: "transcribed",
    },
  ],
  terms: [],
  termInsights: [],
  materials: [],
  currentNote: null,
  sourceGroups: [],
};
const health = {
  mongo: true,
  gemini: false,
  jev: false,
  recordingSourceSelection: true,
  localCapture: true,
  uploadStreams: true,
};
// Every write the API received, in order: accepted chunks and stream updates.
const writes = [];
const attempts = [];
const errors = [];
let upload = "accept";

function multipart(request) {
  const boundary =
    "--" + /boundary=(.+)$/.exec(request.headers()["content-type"])[1];
  const body = request.postDataBuffer();
  assert(body, "The chunk upload must carry its multipart body");
  const fields = {};
  let file;
  for (
    let start = body.indexOf(boundary) + boundary.length + 2,
      end = body.indexOf(boundary, start);
    end > 0;
    start = end + boundary.length + 2, end = body.indexOf(boundary, start)
  ) {
    const part = body.subarray(start, end - 2);
    const split = part.indexOf("\r\n\r\n");
    const head = part.subarray(0, split).toString();
    const content = part.subarray(split + 4);
    if (/filename=/.test(head)) file = Buffer.from(content);
    else fields[/name="([^"]+)"/.exec(head)[1]] = content.toString();
  }
  return { fields, file };
}

const browser = await launchBrowser([
  "--use-fake-device-for-media-stream",
  "--use-fake-ui-for-media-stream",
  "--autoplay-policy=no-user-gesture-required",
]);
const context = await browser.newContext({
  viewport: { width: 1440, height: 1000 },
  permissions: ["microphone"],
});
await context.route(
  (url) => url.protocol === "https:" || url.hostname !== "127.0.0.1",
  (route) => route.abort(),
);
// A screen share with a 440 Hz tone that sounds for 1.2 s and pauses 1 s, or with no audio at all.
await context.addInitScript(() => {
  window.__share = { audio: true, tracks: [] };
  navigator.mediaDevices.getDisplayMedia = async () => {
    const audio = new AudioContext();
    const tone = audio.createOscillator();
    const gain = audio.createGain();
    const output = audio.createMediaStreamDestination();
    tone.frequency.value = 440;
    tone.connect(gain).connect(output);
    tone.start();
    for (let at = 0; at < 120; at += 2.2) {
      gain.gain.setValueAtTime(0.3, audio.currentTime + at);
      gain.gain.setValueAtTime(0, audio.currentTime + at + 1.2);
    }
    const canvas = document.createElement("canvas");
    canvas.getContext("2d").fillRect(0, 0, 1, 1);
    const tracks = [
      ...(window.__share.audio ? output.stream.getAudioTracks() : []),
      ...canvas.captureStream(1).getVideoTracks(),
    ];
    window.__share.tracks = tracks;
    return new MediaStream(tracks);
  };
  // What the browser does when the user presses “Stop sharing”.
  window.__endShare = () => {
    for (const track of window.__share.tracks)
      track.dispatchEvent(new Event("ended"));
  };
});
const page = await context.newPage();
page.on("pageerror", (error) => errors.push(error.message));
await page.route("**/api/**", async (route) => {
  const request = route.request(),
    path = new URL(request.url()).pathname;
  if (path === "/api/chunks" && request.method() === "POST") {
    const { fields, file } = multipart(request);
    attempts.push(fields);
    if (upload === "offline") return route.abort("internetdisconnected");
    if (upload === "fail-once") {
      upload = "accept";
      return route.fulfill({
        status: 503,
        json: { error: "Local MongoDB is unavailable" },
      });
    }
    writes.push({ kind: "chunk", fields, file });
    session.chunks.push({
      id: `${fields.sessionId}-${fields.sourceId}-${fields.sequence}-${fields.sha256}`,
      streamId: fields.streamId,
      sourceId: fields.sourceId,
      sequence: Number(fields.sequence),
      startMs: Number(fields.startMs),
      endMs: Number(fields.endMs),
      recordedAt: fields.recordedAt,
      status: "pending-asr",
    });
    return route.fulfill({
      status: 202,
      json: { id: session.chunks.at(-1).id, status: "pending-asr" },
    });
  }
  const stream = /^\/api\/sessions\/([a-f0-9]{32})\/streams\/([^/]+)$/.exec(
    path,
  );
  if (stream && request.method() === "PUT") {
    if (upload === "offline") return route.abort("internetdisconnected");
    writes.push({
      kind: "stream",
      sessionId: stream[1],
      streamId: stream[2],
      ...request.postDataJSON(),
    });
    return route.fulfill({ json: { streamId: stream[2] } });
  }
  if (request.method() !== "GET")
    return route.fulfill({
      status: 404,
      json: { error: `Unexpected ${request.method()} ${path}` },
    });
  let json;
  if (path === "/api/health") json = health;
  else if (path === "/api/capture/status") json = { state: "idle", bytes: {} };
  else if (path === "/api/sessions")
    json = [{ id, title: session.title, groupId: null }];
  else if (path === "/api/groups") json = [];
  else if (path === `/api/sessions/${id}`) json = session;
  else if (path.endsWith("/activity") || path.endsWith("/conversations"))
    json = [];
  else return route.fulfill({ status: 404, json: { error: "Not in fixture" } });
  await route.fulfill({ json });
});

const until = async (condition, what, ms = 20_000) => {
  const deadline = Date.now() + ms;
  while (!condition()) {
    if (Date.now() > deadline) throw new Error(`Timed out waiting for ${what}`);
    await page.waitForTimeout(100);
  }
};
const chunks = () => writes.filter((write) => write.kind === "chunk");
const streams = () => writes.filter((write) => write.kind === "stream");
const recorder = page.getByTestId("recorder");
const button = (name) => recorder.getByRole("button", { name, exact: true });

try {
  await page.goto(url);
  await recorder.waitFor();
  await page
    .getByRole("banner")
    .getByText("Browser recording", { exact: true })
    .waitFor();

  // Both recorders are offered by a Windows API; the browser one records here.
  const kind = recorder.getByRole("combobox", { name: "Recorder" });
  assert.deepEqual(await kind.locator("option").allTextContents(), [
    "Browser",
    "Windows (API)",
  ]);
  await kind.selectOption("browser");
  await recorder
    .getByRole("combobox", { name: "Recording source" })
    .selectOption("microphone");

  // Microphone: the first upload fails and is sent again; the live row shows while audio is recorded.
  upload = "fail-once";
  await button("Start recording").click();
  await recorder.getByRole("status").getByText("Recording").waitFor();
  await page.getByTestId("live-row").first().waitFor({ timeout: 10_000 });
  await until(() => chunks().length >= 2, "two microphone chunks");
  assert.equal(attempts[0].sha256, chunks()[0].fields.sha256);
  assert.equal(attempts[0].sequence, chunks()[0].fields.sequence);
  const first = streams()[0];
  assert.deepEqual(
    { closed: first.closed, sessionId: first.sessionId },
    { closed: false, sessionId: id },
  );

  await button("Pause recording").click();
  await until(
    () => streams().some((write) => write.closed && !write.stop),
    "the paused stream to close",
  );
  await button("Resume recording").click();
  await until(
    () =>
      streams().some(
        (write) => !write.closed && write.streamId !== first.streamId,
      ),
    "a new stream after resume",
  );
  const resumed = streams().at(-1).streamId;
  await until(
    () => chunks().some((write) => write.fields.streamId === resumed),
    "a chunk after resume",
  );
  await button("Stop recording").click();
  await until(() => streams().some((write) => write.stop), "Stop");
  await button("Start recording").waitFor();

  // Order: each stream's chunks come before its close, and Stop is the last write.
  const order = writes.map((write) =>
    write.kind === "chunk"
      ? `chunk:${write.fields.streamId}`
      : `${write.closed ? (write.stop ? "stop" : "close") : "open"}:${write.streamId}`,
  );
  const closeFirst = order.indexOf(`close:${first.streamId}`);
  assert(closeFirst > order.lastIndexOf(`chunk:${first.streamId}`));
  assert(order.indexOf(`chunk:${resumed}`) > closeFirst);
  assert.equal(order.at(-1), `stop:${resumed}`);
  assert(order.lastIndexOf(`chunk:${resumed}`) < order.length - 1);

  // Each WAV: the fields name it, its hash matches, it is 16 kHz mono 16-bit, and the timeline continues from 30 s.
  let previous = 30_000,
    sequence = 0;
  for (const { fields, file } of chunks()) {
    assert.equal(fields.sessionId, id);
    assert.equal(fields.sourceId, "microphone");
    assert.match(fields.streamId, /^[a-f0-9]{32}$/);
    assert.equal(
      createHash("sha256").update(file).digest("hex"),
      fields.sha256,
    );
    assert.equal(file.toString("ascii", 0, 4), "RIFF");
    assert.equal(file.toString("ascii", 8, 12), "WAVE");
    assert.equal(file.readUInt16LE(20), 1);
    assert.equal(file.readUInt16LE(22), 1);
    assert.equal(file.readUInt32LE(24), 16_000);
    assert.equal(file.readUInt16LE(34), 16);
    assert.equal(Number(fields.startMs), previous);
    const length = Number(fields.endMs) - Number(fields.startMs);
    assert(length > 0 && length <= 8_500, `chunk length ${length}`);
    assert(
      Math.abs((file.length - 44) / 32 - length) < 300,
      "WAV duration matches its time range",
    );
    assert(Number(fields.sequence) > sequence);
    assert(!Number.isNaN(Date.parse(fields.recordedAt)));
    previous = Number(fields.endMs);
    sequence = Number(fields.sequence);
  }
  const microphoneChunks = chunks().length;

  // Offline: chunks wait in IndexedDB, survive a reload and go out in order, followed by Stop.
  upload = "offline";
  await page.waitForTimeout(500);
  await button("Start recording").click();
  await recorder.getByRole("status").getByText("Recording").waitFor();
  await until(
    () => attempts.length > microphoneChunks + 2,
    "an offline attempt",
  );
  await page.waitForTimeout(3_000);
  await button("Stop recording").click();
  await button("Start recording").waitFor();
  const waiting = Number(
    (await recorder.getByTestId("upload-pending").textContent()).slice(1),
  );
  assert(waiting >= 1, "Recorded audio waits to upload while offline");
  await page.reload();
  upload = "accept";
  await recorder.waitFor();
  await until(
    () => streams().filter((write) => write.stop).length === 2,
    "Stop after reload",
    40_000,
  );
  const offline = chunks().slice(microphoneChunks);
  assert.equal(offline.length, waiting);
  assert.equal(Number(offline[0].fields.startMs), previous);
  for (let index = 1; index < offline.length; index++)
    assert.equal(
      offline[index].fields.startMs,
      offline[index - 1].fields.endMs,
    );
  assert.equal(writes.at(-1).kind, "stream");
  await recorder.getByTestId("upload-pending").waitFor({ state: "hidden" });

  // A Linux API offers only the browser recorder; system audio comes from the shared screen and stops with it.
  health.localCapture = false;
  await page.reload();
  await recorder.waitFor();
  assert.equal(
    await recorder.getByRole("combobox", { name: "Recorder" }).count(),
    0,
  );
  await recorder
    .getByRole("combobox", { name: "Recording source" })
    .selectOption("system");
  await button("Start recording").click();
  await recorder.getByRole("status").getByText("Recording").waitFor();
  await until(
    () => chunks().some((write) => write.fields.sourceId === "system"),
    "a system audio chunk",
  );
  const before = writes.length;
  await page.evaluate(() => window.__endShare());
  await button("Start recording").waitFor();
  await page
    .getByRole("alert")
    .getByText(/System audio ended because screen sharing stopped/)
    .waitFor();
  await until(
    () => writes.slice(before).some((write) => write.stop),
    "Stop after sharing ended",
  );
  const system = chunks().filter((write) => write.fields.sourceId === "system");
  assert(system.every((write) => write.file.readUInt32LE(24) === 16_000));

  // A share without audio does not start recording.
  const writesBefore = writes.length;
  await page.evaluate(() => (window.__share.audio = false));
  await button("Start recording").click();
  await page
    .getByRole("alert")
    .getByText(/no audio was shared/)
    .waitFor();
  await button("Start recording").waitFor();
  await page.waitForTimeout(500);
  assert.equal(writes.length, writesBefore);
  assert.deepEqual(errors, []);

  console.log(
    `Browser recording checks passed: ${microphoneChunks} microphone chunks (16 kHz mono WAV, matching SHA-256, timeline from 30 s, retried after 503), pause/resume/stop stream order, ${waiting} chunks kept offline across a reload, ${system.length} system audio chunks until sharing stopped, share without audio refused, recorder choice`,
  );
} finally {
  await context.close();
  await browser.close();
}
