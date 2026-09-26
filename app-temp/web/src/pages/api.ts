import type { ZodType } from "zod";

export async function api<T = unknown>(
  path: string,
  method = "GET",
  body?: unknown,
  schema?: ZodType<T>,
): Promise<T> {
  const response = await fetch("/api" + path, {
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
