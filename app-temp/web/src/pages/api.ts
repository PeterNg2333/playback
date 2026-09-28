import type { ZodType } from "zod";
import { AnswerSchema } from "../types/api";

export async function api<T = unknown>(
  path: string,
  method = "GET",
  body?: unknown,
  schema?: ZodType<T>,
): Promise<T> {
  const response = await fetch("/api" + path, {
    signal: AbortSignal.timeout(150_000),
    method,
    headers: {
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(error.error || `HTTP ${response.status}`);
  }
  const bodyText = await response.text();
  if (!bodyText) {
    if (schema) throw new Error(`Empty response from ${path}`);
    return undefined as T;
  }
  const data: unknown = JSON.parse(bodyText);
  return schema ? schema.parse(data) : (data as T);
}

export async function askStream(sessionId: string, body: unknown, signal: AbortSignal, update: (text: string) => void) {
  const response = await fetch(`/api/sessions/${sessionId}/ask/stream`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
    signal: AbortSignal.any([signal, AbortSignal.timeout(150_000)]),
  });
  if (!response.ok || !response.body) throw new Error(`Ask Playback HTTP ${response.status}`);
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "", answer, size = 0;
  try {
    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      size += value.length;
      if (size > 8_000_000) throw new Error("Answer stream exceeded the size limit");
      buffer += decoder.decode(value, { stream: true });
      let end;
      while ((end = buffer.indexOf("\n")) >= 0) {
        const line = buffer.slice(0, end); buffer = buffer.slice(end + 1);
        if (!line.trim()) continue;
        const event = JSON.parse(line);
        if (event.type === "error") throw new Error(event.error);
        if (event.type === "draft" && typeof event.text === "string") update(event.text);
        if (event.type === "result") answer = AnswerSchema.parse(event.answer);
      }
    }
    if (!answer) throw new Error("The answer stream ended before completion. Your question is retained; retry explicitly.");
    return answer;
  } finally { await reader.cancel(); }
}
