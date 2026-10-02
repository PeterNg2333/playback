import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { formatElapsed } from "../../lib/time";
import { PopupHeader } from "../../components/PopupHeader";
import { CitationChip } from "../../components/CitationChip";

// Cited parts from one audio source within five minutes of each other show as one range.
const MAX_RANGE_MS = 5 * 60_000;

// Plays a cited passage or jumps to one, inside the open panel.
const sourceButton =
  "m-1 cursor-pointer rounded-[5px] border border-line bg-accent-soft p-1.25 text-accent";

// A citation chip that opens the cited passages: their times, text, playback and a jump to each.
export function SourceCitation({
  groups,
  labels,
  details = new Map(),
  onSource,
  onPlay,
}: {
  groups: { id: string; transcriptIds: string[] }[];
  labels: Map<string, string>;
  details?: Map<
    string,
    { startMs?: number; endMs?: number; sourceId?: string; text?: string }
  >;
  onSource?: (id: string) => void;
  onPlay?: (ids: string[]) => void;
}) {
  const [open, setOpen] = useState(false);
  const anchor = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState({ left: 12, top: 12 });
  const ids = [...new Set(groups.flatMap((group) => group.transcriptIds))];
  const ranges: {
    sourceId: string;
    start: number;
    end: number;
    ids: string[];
  }[] = [];
  for (const id of ids
    .slice()
    .sort(
      (a, b) => (details.get(a)?.startMs ?? 0) - (details.get(b)?.startMs ?? 0),
    )) {
    const source = details.get(id);
    if (source?.startMs == null || source.endMs == null) continue;
    const previous = ranges.at(-1);
    if (
      previous &&
      previous.sourceId === source.sourceId &&
      source.startMs >= previous.start &&
      source.endMs - previous.start <= MAX_RANGE_MS
    ) {
      previous.end = Math.max(previous.end, source.endMs);
      previous.ids.push(id);
    } else
      ranges.push({
        sourceId: source.sourceId ?? "audio",
        start: source.startMs,
        end: source.endMs,
        ids: [id],
      });
  }
  const label = ranges.length
    ? `${formatElapsed(ranges[0].start)}–${formatElapsed(ranges[0].end)}${ranges.length > 1 ? ` +${ranges.length - 1}` : ""}`
    : `${ids.length} cited source${ids.length === 1 ? "" : "s"}`;
  useLayoutEffect(() => {
    if (!open || !anchor.current || !panel.current) return;
    const bounds = anchor.current.getBoundingClientRect(),
      size = panel.current.getBoundingClientRect();
    setPosition({
      left: Math.max(12, Math.min(bounds.left, innerWidth - size.width - 12)),
      top: Math.max(
        12,
        Math.min(bounds.bottom + 8, innerHeight - size.height - 12),
      ),
    });
  }, [open]);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (
        !panel.current?.contains(event.target as Node) &&
        !anchor.current?.contains(event.target as Node)
      )
        setOpen(false);
    };
    const escape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
        anchor.current?.focus();
      }
    };
    document.addEventListener("pointerdown", outside);
    document.addEventListener("keydown", escape);
    return () => {
      document.removeEventListener("pointerdown", outside);
      document.removeEventListener("keydown", escape);
    };
  }, [open]);
  return (
    <>
      <CitationChip
        ref={anchor}
        aria-label={`Open audio sources ${label}`}
        aria-expanded={open}
        onClick={() => setOpen(!open)}
      >
        {label}
      </CitationChip>
      {open &&
        createPortal(
          <div
            ref={panel}
            className="fixed z-35 max-h-[min(360px,60dvh)] w-[min(320px,calc(100vw-24px))] overflow-y-auto rounded-[10px] border border-line bg-surface p-3.5 text-[12px] shadow-[0_12px_35px_#25243c25]"
            role="dialog"
            aria-label="Grouped audio sources"
            style={position}
          >
            <PopupHeader
              title="Cited sources"
              closeLabel="Close sources"
              onClose={() => setOpen(false)}
            />
            <small className="text-[smaller]">
              Session elapsed time · mm:ss (hh:mm:ss after one hour). Ranges
              contain only the cited sources; gaps are listed below.
            </small>
            {ranges.map((range, index) => (
              <p className="mb-3" key={index}>
                {range.sourceId} · {formatElapsed(range.start)}–
                {formatElapsed(range.end)} · {range.ids.length} cited parts
              </p>
            ))}
            {groups.map((group) => (
              <section className="mt-3" key={group.id}>
                {onPlay &&
                  group.transcriptIds.some(
                    (id) => details.get(id)?.startMs != null,
                  ) && (
                    <button
                      type="button"
                      className={sourceButton}
                      onClick={() => onPlay(group.transcriptIds)}
                    >
                      Play combined passage
                    </button>
                  )}
                <div>
                  {group.transcriptIds.map((id) => (
                    <div key={id}>
                      {labels.has(id) ? (
                        <button
                          type="button"
                          className={sourceButton}
                          disabled={!onSource}
                          onClick={() => {
                            onSource?.(id);
                            setOpen(false);
                          }}
                        >
                          Jump to {labels.get(id)}
                        </button>
                      ) : (
                        <span>Source unavailable</span>
                      )}
                      {details.get(id)?.text && (
                        <p className="mb-3">{details.get(id)!.text}</p>
                      )}
                      {details.get(id)?.startMs != null && (
                        <small className="text-[smaller]">
                          {details.get(id)!.sourceId} ·{" "}
                          {formatElapsed(details.get(id)!.startMs!)}–
                          {formatElapsed(details.get(id)!.endMs!)} · exact cited
                          part
                        </small>
                      )}
                    </div>
                  ))}
                </div>
              </section>
            ))}
          </div>,
          document.body,
        )}
    </>
  );
}
