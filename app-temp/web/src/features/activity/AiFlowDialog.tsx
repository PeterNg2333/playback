import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { AiFlowSchema, type Group } from "../../lib/backend/schemas";
import { api } from "../../lib/backend/client";
import { Markdown } from "../../components/markdown/Markdown";

// A group's configured AI pipeline and the executions recorded for one of its sessions.
export function AiFlowDialog({
  group,
  onClose,
}: {
  group: Group;
  onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [sessionId, setSessionId] = useState<string>();
  const flow = useQuery({
    queryKey: ["aiFlow", group.id, sessionId],
    queryFn: ({ signal }) =>
      api.get(
        `/groups/${group.id}/flow${sessionId ? "?sessionId=" + sessionId : ""}`,
        AiFlowSchema,
        { signal },
      ),
  });
  // Only a completed read is shown; a reload or a failure hides the previous result.
  const data = flow.isFetching || flow.isError ? undefined : flow.data;
  useEffect(() => {
    dialog.current?.showModal();
    return () => dialog.current?.close();
  }, []);
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
        {flow.isFetching && (
          <p role="status">Loading flow and recorded executions…</p>
        )}
        {flow.isError && !flow.isFetching && (
          <p role="alert">
            {flow.error.message}{" "}
            <button onClick={() => void flow.refetch()}>Retry</button>
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
              <button onClick={() => void flow.refetch()}>
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
