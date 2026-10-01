import clsx from "clsx";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { AiFlowSchema, type Group } from "../../lib/backend/schemas";
import { api } from "../../lib/backend/client";
import { Markdown } from "../../components/markdown/Markdown";

// The dialog's repeated looks: bordered controls and cards, small summaries, and the
// paragraphs, prompts and definitions inside them.
const control =
  "rounded-md border border-line bg-surface px-2.5 py-1.5 text-ink";
const card = "mb-2 rounded-[10px] border border-line p-3";
const summaryStyle = "cursor-pointer text-[12px]";
const paragraph = "mb-3 text-[12px] leading-[1.6]";
const heading = "mb-[1em] text-[1.17em] font-bold";
const code =
  "my-2.75 max-h-75 overflow-auto font-[monospace] text-[11px] whitespace-pre-wrap wrap-anywhere";
const term = "text-[11px] font-bold text-muted";
const definition = "mt-1 mb-3 text-[12px] leading-[1.6]";

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
      className="m-auto h-[90vh] max-h-[92vh] w-[calc(100vw-20px)] max-w-7xl rounded-2xl border border-line bg-surface p-3 text-ink shadow-[0_20px_60px_#25305c30] backdrop:bg-[#20233866] open:flex open:flex-col open:overflow-hidden md:h-[75vh] md:w-[70vw] md:p-5.5 **:data-markdown:max-w-none"
      aria-labelledby="group-flow-title"
      onCancel={onClose}
    >
      <header className="flex shrink-0 items-start justify-between gap-5 bg-surface pb-2.5">
        <div>
          <h2 id="group-flow-title" className="text-[15px] font-[750]">
            {group.name} · AI flow
          </h2>
          <p className={paragraph}>
            Group viewing scope. Each execution and source belongs to its
            selected session.
          </p>
        </div>
        <button
          className={control}
          aria-label="Close AI flow"
          onClick={onClose}
        >
          ×
        </button>
      </header>
      <div className="min-h-0 overflow-auto">
        {flow.isFetching && (
          <p className={paragraph} role="status">
            Loading flow and recorded executions…
          </p>
        )}
        {flow.isError && !flow.isFetching && (
          <p className={paragraph} role="alert">
            {flow.error.message}{" "}
            <button className={control} onClick={() => void flow.refetch()}>
              Retry
            </button>
          </p>
        )}
        {data && (
          <>
            <div className="flex flex-wrap items-center gap-3">
              <label>
                Session
                <select
                  className={clsx(control, "ml-2.5 max-w-full")}
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
              <button className={control} onClick={() => void flow.refetch()}>
                Refresh executions
              </button>
            </div>
            {!data.sessions.length && (
              <p className={paragraph}>
                No sessions in this group. Configuration is shown; there are no
                session executions.
              </p>
            )}
            {data.gate && (
              <details className={card} data-testid="flow-execution">
                <summary className={summaryStyle}>
                  Saved note gate · {data.gate.status}
                  {data.gate.decision ? " · " + data.gate.decision : ""}
                </summary>
                <p className={paragraph}>
                  {data.gate.model || "No provider model recorded"} ·{" "}
                  {data.gate.promptVersion || "No prompt version recorded"} ·{" "}
                  {new Date(data.gate.changedAt).toLocaleString()}
                </p>
                <p className={paragraph}>
                  Wait decisions: {data.gate.waitCount} · Failed attempts:{" "}
                  {data.gate.attempts}
                  {data.gate.retryAt
                    ? " · Retry after " +
                      new Date(data.gate.retryAt).toLocaleString()
                    : ""}
                </p>
                <p className={paragraph}>
                  {data.gate.flushRequested ? "Stop flush is pending. " : ""}
                  {data.gate.generationRequested
                    ? "A generation job was requested."
                    : "No generation job requested for this input."}
                </p>
                <pre className={code}>
                  Input identity: {data.gate.inputHash || "not recorded"}
                </pre>
              </details>
            )}
            <h3 className={heading}>Configured pipeline</h3>
            <Markdown value={graph} />
            <div className="grid grid-cols-1 gap-2.5 md:grid-cols-2">
              {data.agents.map((agent) => (
                <details className={card} key={agent.id}>
                  <summary className={summaryStyle}>
                    <strong>{agent.name}</strong> <span>{agent.status}</span>
                  </summary>
                  <p className={paragraph}>
                    {agent.provider} / {agent.model}
                  </p>
                  <dl className="my-4">
                    <dt className={term}>Trigger</dt>
                    <dd className={definition}>{agent.trigger}</dd>
                    <dt className={term}>Input</dt>
                    <dd className={definition}>{agent.inputRole}</dd>
                    <dt className={term}>Output</dt>
                    <dd className={definition}>{agent.outputRole}</dd>
                    <dt className={term}>Lifecycle</dt>
                    <dd className={definition}>{agent.lifecycle}</dd>
                  </dl>
                  <details>
                    <summary className={summaryStyle}>
                      Effective prompt · {agent.promptVersion}
                    </summary>
                    <pre className={code}>{agent.prompt}</pre>
                  </details>
                </details>
              ))}
            </div>
            <h3 className={heading}>Recorded executions</h3>
            <p className={paragraph}>
              Configuration above does not prove a provider has run. Older
              executions may have no recorded prompt identity or usage.
            </p>
            {!data.executions.length && (
              <p className={paragraph}>
                No recorded executions for this session.
              </p>
            )}
            {data.executions.map((item) => (
              <details
                className={card}
                data-testid="flow-execution"
                key={item.id}
              >
                <summary className={summaryStyle}>
                  <strong>{item.task}</strong> <span>{item.status}</span> ·{" "}
                  {item.durationMs == null
                    ? "in progress"
                    : `${item.durationMs} ms`}
                </summary>
                <p className={paragraph}>
                  {item.provider} / {item.model} ·{" "}
                  {new Date(item.startedAt).toLocaleString()} · base v
                  {item.basedOnVersion ?? "unknown"}
                  {item.sectionId ? " · " + item.sectionId : ""}
                </p>
                <p className={paragraph}>{item.summary}</p>
                <p className={paragraph}>
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
                <p className={paragraph}>
                  {item.sourceIds.length} input sources ·{" "}
                  {item.inputBytes ?? "unknown"} input bytes
                </p>
                <pre className={code}>
                  {item.promptVersion
                    ? `Prompt ${item.promptVersion}\nHash ${item.promptHash}\nInput ${item.inputHash}`
                    : "Prompt identity was not recorded by this build."}
                </pre>
                {item.promptText && (
                  <details>
                    <summary className={summaryStyle}>
                      Recorded effective prompt
                    </summary>
                    <pre className={code}>{item.promptText}</pre>
                  </details>
                )}
                {item.usageJson && (
                  <pre className={code}>Provider usage: {item.usageJson}</pre>
                )}
              </details>
            ))}
          </>
        )}
      </div>
    </dialog>
  );
}
