import { useQuery } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { Dialog, DialogActions, DialogButton } from "../../components/Dialog";
import { api } from "../../lib/backend/client";
import {
  ProcessingPolicySchema,
  ProcessingSettingsSchema,
  type ProcessingOverrides,
  type ProcessingPolicy,
  type ProcessingSettings,
} from "../../lib/backend/schemas";

const inputStyle =
  "rounded-md border border-line bg-white px-2 py-1.5 disabled:opacity-50";
const fields = [
  {
    key: "checkIntervalSeconds",
    label: "Decision check interval",
    min: 2,
    max: 30,
    step: 1,
    unit: "s",
    description: "How often changed input can be considered.",
  },
  {
    key: "maxPendingWaitSeconds",
    label: "Maximum pending wait",
    min: 10,
    max: 120,
    step: 1,
    unit: "s",
    description: "Queues notes by this age; provider time is additional.",
  },
  {
    key: "probabilityThreshold",
    label: "Update probability",
    min: 0,
    max: 1,
    step: 0.01,
    unit: "",
    description: "Minimum Jev probability for an early update.",
  },
  {
    key: "confidenceThreshold",
    label: "Decision confidence",
    min: 0,
    max: 1,
    step: 0.01,
    unit: "",
    description: "Jev confidence in its decision, independent of ASR accuracy.",
  },
  { key: "silenceBoundarySeconds", label: "Speech pause", min: 0.7, max: 10, step: 0.1, unit: "s", description: "A quiet pause on every active source can admit notes early." },
  { key: "boundarySettleSeconds", label: "ASR settle grace", min: 0, max: 5, step: 0.1, unit: "s", description: "Maximum grace for pending audio at the pause boundary." },
  { key: "maxCombinedSeconds", label: "Maximum quiet segment", min: 10, max: 300, step: 1, unit: "s", description: "Bounds derived quiet audio files; original chunks remain available." },
  { key: "organizationChangedRevisions", label: "Changed revisions", min: 1, max: 10, step: 1, unit: "", description: "Actual content or outline changes before organization." },
  { key: "organizationMaxDirtyAgeSeconds", label: "Maximum dirty-note age", min: 30, max: 600, step: 1, unit: "s", description: "Organization can be admitted by this age, even for small notes." },
  { key: "organizationCooldownSeconds", label: "Organization cooldown", min: 15, max: 300, step: 1, unit: "s", description: "Minimum time between organization jobs." },
] satisfies {
  key: keyof ProcessingPolicy;
  label: string;
  min: number;
  max: number;
  step: number;
  unit: string;
  description: string;
}[];

export function ProcessingSettingsDialog({
  sessionId,
  onClose,
}: {
  sessionId: string;
  onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [scope, setScope] = useState<"session" | "workspace">("session");
  const [loaded, setLoaded] = useState<ProcessingSettings | null>(null);
  const [overrides, setOverrides] = useState<ProcessingOverrides>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const path =
    scope === "workspace"
      ? "/settings/processing"
      : `/sessions/${sessionId}/processing-settings`;
  const query = useQuery({
    queryKey: ["processingSettings", sessionId, scope],
    queryFn: ({ signal }) =>
      api.get(path, ProcessingSettingsSchema, { signal }),
  });
  useEffect(() => {
    dialog.current?.showModal();
  }, []);
  useEffect(() => {
    if (!loaded && query.data && !query.isFetching) {
      setLoaded(query.data);
      setOverrides(query.data.overrides);
    }
  }, [loaded, query.data, query.isFetching]);
  const effective =
    loaded &&
    ({
      ...loaded.inherited,
      ...Object.fromEntries(
        Object.entries(overrides).filter(([, value]) => value != null),
      ),
    } as ProcessingPolicy);
  const validation = effective
    ? ProcessingPolicySchema.safeParse(effective)
    : null;
  const set = (
    key: keyof ProcessingPolicy,
    value: number | boolean | string | undefined,
  ) => setOverrides((current) => ({ ...current, [key]: value }));
  async function save() {
    if (!loaded || !validation?.success) return;
    setSaving(true);
    setError("");
    try {
      const result = await api.put(
        path,
        {
          expectedRevision: loaded.revision,
          expectedInheritedRevision: loaded.inheritedRevision,
          overrides,
        },
        ProcessingSettingsSchema,
      );
      setLoaded(result);
      setOverrides(result.overrides);
      await query.refetch();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setSaving(false);
    }
  }
  async function reload() {
    setError("");
    const result = await query.refetch();
    if (result.data) {
      setLoaded(result.data);
      setOverrides(result.data.overrides);
    }
  }
  const inherit = (key: keyof ProcessingPolicy) =>
    scope === "session" && (
      <label className="flex items-center gap-1 text-[11px] text-muted">
        <input
          type="checkbox"
          checked={overrides[key] != null}
          disabled={saving}
          onChange={(event) =>
            set(key, event.target.checked ? loaded!.inherited[key] : undefined)
          }
        />
        Override
      </label>
    );
  return (
    <Dialog
      ref={dialog}
      aria-labelledby="processing-title"
      onCancel={(event) => {
        if (saving) event.preventDefault();
      }}
      onClose={onClose}
    >
      <div className="max-h-[85vh] overflow-y-auto p-5 text-[12px]">
        <h2 id="processing-title" className="mb-3 text-base font-bold">
          Notes settings
        </h2>
        <label className="flex items-center justify-between gap-2">
          Apply to
          <select
            className={inputStyle}
            disabled={saving}
            value={scope}
            onChange={(event) => {
              setScope(event.target.value as typeof scope);
              setLoaded(null);
              setError("");
            }}
          >
            <option value="session">This session</option>
            <option value="workspace">Workspace defaults</option>
          </select>
        </label>
        <p className="my-2 text-muted">
          {scope === "session"
            ? "Unchecked fields use workspace defaults."
            : "Applies to inherited fields in this workspace's sessions."}{" "}
          Saved changes apply to the next eligible operation.
        </p>
        {query.error && <p role="alert">{query.error.message}</p>}
        {!loaded ? (
          <p role="status">Loading settings…</p>
        ) : (
          <>
            <div className="my-3 flex flex-wrap gap-2">
              <DialogButton disabled={saving} onClick={() => setOverrides({})}>
                {scope === "session"
                  ? "Use workspace defaults"
                  : "Reset to server defaults"}
              </DialogButton>
              <DialogButton disabled={saving} onClick={() => void reload()}>
                Reload saved settings
              </DialogButton>
            </div>
            <h3 className="mt-4 font-bold">Timing</h3>
            {fields.slice(0, 2).map((field) => numberField(field))}
            <h3 className="mt-4 font-bold">Jev decision</h3>
            <div className="my-3">
              <div className="flex justify-between gap-2">
                <span>Enable Jev gate</span>
                {inherit("jevEnabled")}
              </div>
              <label className="mt-1 flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={effective!.jevEnabled}
                  disabled={
                    saving ||
                    (scope === "session" && overrides.jevEnabled == null)
                  }
                  onChange={(event) => set("jevEnabled", event.target.checked)}
                />
                Allow Jev to admit notes before the deadline
              </label>
              <p className="mt-1 text-muted">
                When off, local batching sends notes directly to Gemini.
              </p>
            </div>
            <details>
              <summary className="cursor-pointer font-bold">
                Advanced thresholds
              </summary>
              {fields.slice(2, 4).map((field) => numberField(field))}
            </details>
            <h3 className="mt-4 font-bold">Speech pauses and quiet audio</h3>
            {fields.slice(4, 7).map((field) => numberField(field))}
            <div className="my-3 flex justify-between gap-2"><label className="flex items-center gap-2">
              <input type="checkbox" checked={effective!.combineQuiet}
                disabled={saving || (scope === "session" && overrides.combineQuiet == null)}
                onChange={event => set("combineQuiet", event.target.checked)} />Combine adjacent quiet audio
            </label>{inherit("combineQuiet")}</div>
            <h3 className="mt-4 font-bold">Organization</h3>
            <div className="my-3 flex justify-between gap-2"><label className="flex items-center gap-2"><input type="checkbox" checked={effective!.organizationEnabled}
              disabled={saving || (scope === "session" && overrides.organizationEnabled == null)} onChange={event => set("organizationEnabled", event.target.checked)} />Organize automatically</label>{inherit("organizationEnabled")}</div>
            {fields.slice(7).map(field => numberField(field))}
            {choiceField("organizationScope", "Organization scope", [["affected-topics", "Affected topics"], ["document-outline", "Document outline"]])}
            {choiceField("notePresentation", "Note presentation", [["numbered-hierarchy", "Numbered hierarchy"], ["headings", "Headings"]])}
            <p className="my-2 text-muted">Automatic organization preserves text and citations. Rewrites require a reviewed preview.</p>
            {!validation?.success && (
              <p className="mt-3 text-red-700" role="alert">
                {validation?.error.issues[0]?.message}
              </p>
            )}
            {error && (
              <p className="mt-3 text-red-700" role="alert">
                {error} Your changes are retained.
              </p>
            )}
          </>
        )}
        <DialogActions>
          <DialogButton
            disabled={saving}
            onClick={() => dialog.current?.close()}
          >
            Close
          </DialogButton>
          <DialogButton
            variant="primary"
            disabled={saving || !loaded || !validation?.success}
            onClick={() => void save()}
          >
            {saving ? "Saving…" : "Save settings"}
          </DialogButton>
        </DialogActions>
      </div>
    </Dialog>
  );
  function numberField(field: (typeof fields)[number]) {
    const value = effective![field.key] as number;
    const enabled = scope === "workspace" || overrides[field.key] != null;
    return (
      <div key={field.key} className="my-3">
        <div className="flex justify-between gap-2">
          <label htmlFor={`processing-${field.key}`}>{field.label}</label>
          {inherit(field.key)}
        </div>
        <div className="mt-1 flex items-center gap-2">
          <input
            aria-label={`${field.label} slider`}
            className="min-w-0 flex-1 accent-accent"
            type="range"
            min={field.min}
            max={field.max}
            step={field.step}
            value={Number.isFinite(value) ? value : field.min}
            disabled={saving || !enabled}
            onChange={(event) => set(field.key, Number(event.target.value))}
          />
          <input
            id={`processing-${field.key}`}
            className={`${inputStyle} w-20`}
            type="number"
            min={field.min}
            max={field.max}
            step={field.step}
            value={Number.isFinite(value) ? value : ""}
            disabled={saving || !enabled}
            onChange={(event) =>
              set(
                field.key,
                event.target.value === "" ? NaN : Number(event.target.value),
              )
            }
          />
          <span>{field.unit}</span>
        </div>
        <p className="mt-1 text-muted">{field.description}</p>
      </div>
    );
  }
  function choiceField(key: "organizationScope" | "notePresentation", label: string, choices: string[][]) {
    return <div className="my-3"><div className="flex justify-between gap-2"><label htmlFor={`processing-${key}`}>{label}</label>{inherit(key)}</div>
      <select id={`processing-${key}`} className={`${inputStyle} mt-1 w-full`} value={effective![key]} disabled={saving || (scope === "session" && overrides[key] == null)} onChange={event => set(key, event.target.value)}>
        {choices.map(([value, text]) => <option key={value} value={value}>{text}</option>)}
      </select></div>;
  }
}
