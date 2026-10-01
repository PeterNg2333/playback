import { useEffect, useMemo, useRef, useState } from "react";
import { z } from "zod";
import { ActivitySchema, type Group } from "../../types/api";
import { api } from "../../pages/api";
import { Markdown } from "../../Component/Markdown";

const FlowSchema = z.object({
  selectedSessionId: z.string().nullish(),
  sessions: z.array(z.object({ id: z.string(), title: z.string() })),
  agents: z.array(
    z.object({
      id: z.string(),
      name: z.string(),
      provider: z.string(),
      model: z.string(),
      status: z.string(),
      promptVersion: z.string(),
      prompt: z.string(),
      trigger: z.string(),
      lifecycle: z.string(),
      inputRole: z.string(),
      outputRole: z.string(),
      dependsOn: z.array(z.string()),
    }),
  ),
  executions: ActivitySchema.array(),
  automaticOrganization: z.boolean(),
  gate: z
    .object({
      status: z.string(),
      decision: z.string(),
      inputHash: z.string(),
      model: z.string(),
      promptVersion: z.string(),
      waitCount: z.number(),
      attempts: z.number(),
      retryAt: z.string().nullish(),
      changedAt: z.string(),
      flushRequested: z.boolean(),
      generationRequested: z.boolean(),
    })
    .nullish(),
});
export function AiFlowDialog({
  group,
  onClose,
}: {
  group: Group;
  onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [sessionId, setSessionId] = useState<string>();
  const [state, setState] = useState<{
    groupId: string;
    data?: z.infer<typeof FlowSchema>;
    error?: string;
    pending?: boolean;
  }>();
  const [reload, setReload] = useState(0);
  useEffect(() => {
    dialog.current?.showModal();
    return () => dialog.current?.close();
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    setState({ groupId: group.id, pending: true });
    api(
      `/groups/${group.id}/flow${sessionId ? "?sessionId=" + sessionId : ""}`,
      "GET",
      undefined,
      FlowSchema,
      controller.signal,
    )
      .then((data) => {
        if (!controller.signal.aborted) setState({ groupId: group.id, data });
      })
      .catch((e) => {
        if (!controller.signal.aborted)
          setState({ groupId: group.id, error: e.message });
      });
    return () => controller.abort();
  }, [group.id, sessionId, reload]);
  const data = state?.groupId === group.id ? state.data : undefined;
  const graph = useMemo(() => {
    if (!data) return "";
    const lines = data.agents.map(
      (x) => `${x.id}["${x.name.replaceAll('"', "'")} · ${x.status}"]`,
    );
    for (const agent of data.agents)
      for (const parent of agent.dependsOn)
        lines.push(`${parent} --> ${agent.id}`);
    return "```mermaid\nflowchart LR\n" + lines.join("\n") + "\n```";
  }, [data?.agents]);
  return (
    <dialog
      ref={dialog}
      className="group-flow-dialog"
      aria-labelledby="group-flow-title"
      onCancel={onClose}
    >
      <header>
        <div>
          <h2 id="group-flow-title">{group.name} · AI flow</h2>
          <p>
            Group viewing scope. Each execution and source belongs to its
            selected session.
          </p>
        </div>
        <button aria-label="Close AI flow" onClick={onClose}>
          ×
        </button>
      </header>
      <div className="flow-body">
        {state?.pending && (
          <p role="status">Loading flow and recorded executions…</p>
        )}
        {state?.error && (
          <p role="alert">
            {state.error}{" "}
            <button onClick={() => setReload((x) => x + 1)}>Retry</button>
          </p>
        )}
        {data && (
          <>
            <div className="flow-controls">
              <label>
                Session
                <select
                  value={sessionId ?? data.selectedSessionId ?? ""}
                  onChange={(e) => setSessionId(e.target.value)}
                >
                  {data.sessions.map((x) => (
                    <option key={x.id} value={x.id}>
                      {x.title}
                    </option>
                  ))}
                </select>
              </label>
              <button onClick={() => setReload((x) => x + 1)}>
                Refresh executions
              </button>
            </div>
            {!data.sessions.length && (
              <p>
                No sessions in this group. Configuration is shown; there are no
                session executions.
              </p>
            )}
            {data.gate && (
              <details className="flow-execution">
                <summary>
                  Saved note gate · {data.gate.status}
                  {data.gate.decision ? " · " + data.gate.decision : ""}
                </summary>
                <p>
                  {data.gate.model || "No provider model recorded"} ·{" "}
                  {data.gate.promptVersion || "No prompt version recorded"} ·{" "}
                  {new Date(data.gate.changedAt).toLocaleString()}
                </p>
                <p>
                  Wait decisions: {data.gate.waitCount} · Failed attempts:{" "}
                  {data.gate.attempts}
                  {data.gate.retryAt
                    ? " · Retry after " +
                      new Date(data.gate.retryAt).toLocaleString()
                    : ""}
                </p>
                <p>
                  {data.gate.flushRequested ? "Stop flush is pending. " : ""}
                  {data.gate.generationRequested
                    ? "A generation job was requested."
                    : "No generation job requested for this input."}
                </p>
                <pre>
                  Input identity: {data.gate.inputHash || "not recorded"}
                </pre>
              </details>
            )}
            <h3>Configured pipeline</h3>
            <Markdown value={graph} />
            <div className="flow-agents">
              {data.agents.map((agent) => (
                <details key={agent.id}>
                  <summary>
                    <strong>{agent.name}</strong> <span>{agent.status}</span>
                  </summary>
                  <p>
                    {agent.provider} / {agent.model}
                  </p>
                  <dl>
                    <dt>Trigger</dt>
                    <dd>{agent.trigger}</dd>
                    <dt>Input</dt>
                    <dd>{agent.inputRole}</dd>
                    <dt>Output</dt>
                    <dd>{agent.outputRole}</dd>
                    <dt>Lifecycle</dt>
                    <dd>{agent.lifecycle}</dd>
                  </dl>
                  <details>
                    <summary>Effective prompt · {agent.promptVersion}</summary>
                    <pre>{agent.prompt}</pre>
                  </details>
                </details>
              ))}
            </div>
            <h3>Recorded executions</h3>
            <p>
              Configuration above does not prove a provider has run. Older
              executions may have no recorded prompt identity or usage.
            </p>
            {!data.executions.length && (
              <p>No recorded executions for this session.</p>
            )}
            {data.executions.map((item) => (
              <details className="flow-execution" key={item.id}>
                <summary>
                  <strong>{item.task}</strong>{" "}
                  <span data-status={item.status}>{item.status}</span> ·{" "}
                  {item.durationMs == null
                    ? "in progress"
                    : `${item.durationMs} ms`}
                </summary>
                <p>
                  {item.provider} / {item.model} ·{" "}
                  {new Date(item.startedAt).toLocaleString()} · base v
                  {item.basedOnVersion ?? "unknown"}
                  {item.sectionId ? " · " + item.sectionId : ""}
                </p>
                <p>{item.summary}</p>
                <p>
                  Schedule:{" "}
                  {item.scheduleDelayMs == null
                    ? "not recorded"
                    : `${item.scheduleDelayMs} ms`}{" "}
                  · Queue:{" "}
                  {item.queueDelayMs == null
                    ? "not recorded"
                    : `${item.queueDelayMs} ms`}{" "}
                  · Provider:{" "}
                  {item.providerLatencyMs == null
                    ? "not recorded"
                    : `${item.providerLatencyMs} ms`}
                </p>
                <p>
                  {item.sourceIds.length} input sources ·{" "}
                  {item.inputBytes ?? "unknown"} input bytes
                </p>
                <pre>
                  {item.promptVersion
                    ? `Prompt ${item.promptVersion}\nHash ${item.promptHash}\nInput ${item.inputHash}`
                    : "Prompt identity was not recorded by this build."}
                </pre>
                {item.promptText && (
                  <details>
                    <summary>Recorded effective prompt</summary>
                    <pre>{item.promptText}</pre>
                  </details>
                )}
                {item.usageJson && <pre>Provider usage: {item.usageJson}</pre>}
              </details>
            ))}
          </>
        )}
      </div>
    </dialog>
  );
}
