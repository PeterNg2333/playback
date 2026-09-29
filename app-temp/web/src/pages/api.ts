import type { ZodType } from "zod";
import { AnswerSchema } from "../types/api";

function requestScope(signal?: AbortSignal) {
  const controller = new AbortController();
  const forward = () => controller.abort(signal?.reason);
  if (signal?.aborted) forward(); else signal?.addEventListener("abort", forward, { once: true });
  const timer = setTimeout(() => controller.abort(new DOMException("Request timed out", "TimeoutError")), 150_000);
  return { signal: controller.signal, dispose: () => { clearTimeout(timer); signal?.removeEventListener("abort", forward); } };
}
async function fetchApi(path: string, options: RequestInit) {
  try { return await fetch("/api" + path, options); }
  catch (reason) {
    if (reason instanceof TypeError)
      throw new Error("Cannot reach the Playback backend. Start the server and retry. Your question is retained.");
    throw reason;
  }
}

async function responseError(response: Response) {
  const error = await response.json().catch(() => ({}));
  return new Error(error.error || (response.status >= 500
    ? "The Playback backend is unavailable or did not respond. Start the server and retry. Your question is retained."
    : `HTTP ${response.status}`));
}

export async function api<T = unknown>(
  path: string,
  method = "GET",
  body?: unknown,
  schema?: ZodType<T>,
  signal?: AbortSignal,
): Promise<T> {
  const scope = requestScope(signal);
  try {
    const response = await fetchApi(path, {
      signal: scope.signal,
      method,
      headers: {
        ...(body ? { "Content-Type": "application/json" } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });
    if (!response.ok) {
      throw await responseError(response);
    }
    const bodyText = await response.text();
    if (!bodyText) {
      if (schema) throw new Error(`Empty response from ${path}`);
      return undefined as T;
    }
    const data: unknown = JSON.parse(bodyText);
    return schema ? schema.parse(data) : (data as T);
  } finally { scope.dispose(); }
}

export async function askStream(sessionId: string, body: unknown, signal: AbortSignal, update: (text: string) => void) {
  const scope = requestScope(signal);
  try {
    const response = await fetchApi(`/sessions/${sessionId}/ask/stream`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body),
      signal: scope.signal,
    });
    if (!response.ok) throw await responseError(response);
    if (!response.body) throw new Error("The answer stream is unavailable. Your question is retained; retry explicitly.");
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
  } finally { scope.dispose(); }
}
