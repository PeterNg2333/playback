import type { ZodType } from "zod";
import { AnswerSchema } from "./schemas";

// Calls to the local Playback API. Each request has a time limit and a readable error, and a
// response is checked against its schema here, at the boundary.

const REQUEST_TIMEOUT_MS = 150_000;
const MAX_ANSWER_STREAM_BYTES = 8_000_000;

type RequestOptions = { signal?: AbortSignal };

export const api = {
  get: <T = unknown>(
    path: string,
    schema?: ZodType<T>,
    options?: RequestOptions,
  ) => request("GET", path, undefined, schema, options?.signal),
  post: <T = unknown>(
    path: string,
    body?: unknown,
    schema?: ZodType<T>,
    options?: RequestOptions,
  ) => request("POST", path, body, schema, options?.signal),
  put: <T = unknown>(
    path: string,
    body: unknown,
    schema?: ZodType<T>,
    options?: RequestOptions,
  ) => request("PUT", path, body, schema, options?.signal),
  delete: (path: string, options?: RequestOptions) =>
    request("DELETE", path, undefined, undefined, options?.signal),
};

function requestScope(signal?: AbortSignal) {
  const controller = new AbortController();
  const forward = () => controller.abort(signal?.reason);
  if (signal?.aborted) forward();
  else signal?.addEventListener("abort", forward, { once: true });
  const timer = setTimeout(
    () =>
      controller.abort(new DOMException("Request timed out", "TimeoutError")),
    REQUEST_TIMEOUT_MS,
  );
  return {
    signal: controller.signal,
    dispose: () => {
      clearTimeout(timer);
      signal?.removeEventListener("abort", forward);
    },
  };
}
async function fetchApi(path: string, options: RequestInit) {
  try {
    const headers = new Headers(options.headers);
    const token = csrfToken();
    if (token && options.method && options.method !== "GET")
      headers.set("X-CSRF-TOKEN", token);
    const response = await fetch("/api" + path, {
      ...options,
      headers,
      credentials: "same-origin",
    });
    if (response.status === 401 && token) window.location.assign("/login");
    return response;
  } catch (reason) {
    if (reason instanceof TypeError)
      throw new Error(
        "Cannot reach the Playback backend. Start the server and retry. Your question is retained.",
      );
    throw reason;
  }
}

export function csrfToken() {
  const cookie = document.cookie
    .split("; ")
    .find((value) => value.startsWith("Playback.Csrf="));
  return cookie
    ? decodeURIComponent(cookie.slice("Playback.Csrf=".length))
    : undefined;
}

async function responseError(response: Response) {
  const error = await response.json().catch(() => ({}));
  return new Error(
    error.error ||
      (response.status >= 500
        ? "The Playback backend is unavailable or did not respond. Start the server and retry. Your question is retained."
        : `HTTP ${response.status}`),
  );
}

async function request<T>(
  method: string,
  path: string,
  body: unknown,
  schema: ZodType<T> | undefined,
  signal: AbortSignal | undefined,
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
  } finally {
    scope.dispose();
  }
}

export async function askStream(
  sessionId: string,
  body: unknown,
  signal: AbortSignal,
  update: (text: string) => void,
) {
  const scope = requestScope(signal);
  try {
    const response = await fetchApi(`/sessions/${sessionId}/ask/stream`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
      signal: scope.signal,
    });
    if (!response.ok) throw await responseError(response);
    if (!response.body)
      throw new Error(
        "The answer stream is unavailable. Your question is retained; retry explicitly.",
      );
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "",
      answer,
      size = 0;
    try {
      while (true) {
        const { value, done } = await reader.read();
        if (done) break;
        size += value.length;
        if (size > MAX_ANSWER_STREAM_BYTES)
          throw new Error("Answer stream exceeded the size limit");
        buffer += decoder.decode(value, { stream: true });
        let end;
        while ((end = buffer.indexOf("\n")) >= 0) {
          const line = buffer.slice(0, end);
          buffer = buffer.slice(end + 1);
          if (!line.trim()) continue;
          const event = JSON.parse(line);
          if (event.type === "error") throw new Error(event.error);
          if (event.type === "draft" && typeof event.text === "string")
            update(event.text);
          if (event.type === "result")
            answer = AnswerSchema.parse(event.answer);
        }
      }
      if (!answer)
        throw new Error(
          "The answer stream ended before completion. Your question is retained; retry explicitly.",
        );
      return answer;
    } finally {
      await reader.cancel();
    }
  } finally {
    scope.dispose();
  }
}
