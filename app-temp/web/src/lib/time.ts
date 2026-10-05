// How times read on screen: dates, time of day, and elapsed time within a session.

export function formatDateTime(value?: string) {
  return value ? new Date(value).toLocaleString() : "Time not recorded";
}

// Elapsed time within a session: "05:03", or "01:02:03" from the first hour on.
export function formatElapsed(ms: number) {
  const seconds = Math.floor(ms / 1000);
  const clock = `${String(Math.floor((seconds % 3600) / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
  return seconds >= 3600
    ? `${String(Math.floor(seconds / 3600)).padStart(2, "0")}:${clock}`
    : clock;
}

// Elapsed minutes and seconds with no hour part: "75:03".
export function formatMinutes(ms: number) {
  return `${Math.floor(ms / 60000)}:${String(Math.floor(ms / 1000) % 60).padStart(2, "0")}`;
}

// Time of day: "14:05:09".
export function clockTime(date: Date) {
  return `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}:${String(date.getSeconds()).padStart(2, "0")}`;
}

// Time of day at which a recorded part starts and ends. Parts saved without a recording
// time are placed from the session's creation time.
export function recordedRange(
  recordedAt: string | null | undefined,
  sessionCreatedAt: string | undefined,
  startMs: number,
  endMs: number,
) {
  const start = recordedAt
    ? new Date(recordedAt)
    : new Date(new Date(sessionCreatedAt || 0).getTime() + startMs);
  const end = new Date(start.getTime() + endMs - startMs);
  return { start: clockTime(start), end: clockTime(end) };
}
