import { useMemo, useRef } from "react";
import { Markdown, indexMarkdownSources } from "../Component/Markdown";
import { askAboutTerm } from "../features/ask/askAbout";
import type { Evidence, Session } from "../types/api";
import type { AudioPlayer } from "./useAudioPlayback";
import { SourceCitation } from "../Component/SourceCitation";
import { TermHighlight } from "./TermHighlight";
import { recordedRange } from "./format";

const noRows: never[] = [];
function useCitedRows<T extends { id: string }>(
  rows: T[] | undefined,
  ids: Set<string>,
) {
  const previous = useRef<T[]>([]);
  return useMemo(() => {
    const selected = (rows ?? noRows).filter((row) => ids.has(row.id));
    if (
      selected.length === previous.current.length &&
      selected.every((row, i) => row === previous.current[i])
    )
      return previous.current;
    previous.current = selected;
    return selected;
  }, [rows, ids]);
}
export function NotePreview({
  session,
  markdown,
  reading,
  onReference,
  onOpenSource,
  onPlay: play,
}: {
  session: Session | null;
  markdown: string;
  reading: boolean;
  onReference: (id: string) => void;
  onOpenSource: (source: Evidence) => void;
  onPlay: AudioPlayer["togglePlayback"];
}) {
  const groups = useMemo(
    () => [
      ...(session?.sourceGroups ?? []),
      ...(session?.currentNote?.citations ?? []).map((c) => ({
        id: c.id,
        transcriptIds: c.sourceIds,
      })),
    ],
    [session?.sourceGroups, session?.currentNote?.citations],
  );
  const cited = useMemo(() => {
    const ids = new Set<string>();
    const aliases = new Map(
      groups.map((group) => [group.id, group.transcriptIds]),
    );
    for (const match of markdown.matchAll(
      /\[\[([^\]\n]{1,16000})\]\]|\[([^\]\n]{1,16000})\]/g,
    )) {
      for (const token of (match[1] ?? match[2]).match(/[a-z0-9_-]+/gi) ?? []) {
        ids.add(token);
        for (const source of aliases.get(token) ?? []) ids.add(source);
      }
    }
    for (const section of session?.currentNote?.sections ?? [])
      for (const point of section.points)
        for (const id of point.sourceIds) ids.add(id);
    for (const id of [...ids]) {
      const parent = /^([a-f0-9]{32})_p\d+_\d+_[a-f0-9]{12}$/.exec(id)?.[1];
      if (parent) ids.add(parent);
    }
    return ids;
  }, [markdown, groups, session?.currentNote?.sections]);
  const transcripts = useCitedRows(session?.transcripts, cited),
    materials = useCitedRows(session?.materials, cited);
  const sources = useMemo(
    () => [
      ...transcripts.map((entry) => ({
        id: entry.id,
        label: recordedRange(
          entry.recordedAt,
          session?.createdAt,
          entry.startMs,
          entry.endMs,
        ).start,
        startMs: entry.startMs,
        endMs: entry.endMs,
        sourceId: entry.sourceId,
        text: entry.displayOriginal ?? entry.original,
      })),
      ...materials.map((entry) => ({
        id: entry.id,
        label: entry.name,
        text: entry.text,
      })),
      ...[
        ...new Set(
          session?.currentNote?.citations?.flatMap((c) => c.sourceIds) ?? [],
        ),
      ].flatMap((id) => {
        const match = /^([a-f0-9]{32})_p(\d+)_(\d+)_[a-f0-9]{12}$/.exec(id);
        const material = match && materials.find((x) => x.id === match[1]);
        return material && match
          ? [
              {
                id,
                label: `${material.name} · characters ${+match[2] + 1}–${match[3]}`,
                text: material.text.slice(+match[2], +match[3]),
              },
            ]
          : [];
      }),
    ],
    [
      transcripts,
      materials,
      session?.createdAt,
      session?.currentNote?.citations,
    ],
  );
  const terms = useMemo(
    () =>
      (session?.termInsights ?? []).filter(
        (x) =>
          x.highlight &&
          !["details", "detail", "okay", "information"].includes(
            x.term.toLowerCase(),
          ) &&
          (!x.outputLanguage || x.outputLanguage === session?.noteLanguage),
      ),
    [session?.termInsights, session?.noteLanguage],
  );
  const termLabels = useMemo(
    () => terms.map((x) => ({ id: x.id, term: x.term })),
    [terms],
  );
  const references = useMemo(
    () => indexMarkdownSources(sources, groups),
    [sources, groups],
  );
  const { sourceLabels: labels, sourceDetails: details } = references;
  const onPlay = (ids: string[]) =>
    play(
      "note-passage-" + ids.join("-"),
      (session?.transcripts ?? []).filter((x) => ids.includes(x.id)),
    );
  const onSource = (id: string) => {
    const parent = /^([a-f0-9]{32})_p\d+_\d+_[a-f0-9]{12}$/.exec(id)?.[1] ?? id;
    onOpenSource({
      kind: session?.materials.some((x) => x.id === parent)
        ? "material"
        : "lecture",
      id: parent,
    });
  };
  const renderTerm = (id: string, text: string) => {
    const insight = terms.find((x) => x.id === id);
    return insight && session ? (
      <TermHighlight
        sessionId={session.id}
        insight={insight}
        text={text}
        onAsk={() =>
          askAboutTerm(session, {
            text: insight.term,
            transcriptIds: insight.transcriptIds,
            materialIds: insight.materialIds,
          })
        }
      />
    ) : (
      text
    );
  };
  const sections =
    markdown === session?.noteMarkdown
      ? session.currentNote?.sections
      : undefined;
  const common = {
    references,
    terms: termLabels,
    renderTerm,
    onSource,
    onReference,
    onPlaySources: onPlay,
  };
  return (
    <div className="note-content">
      {sections?.length ? (
        sections.map((section) => {
          const ids = [...new Set(section.points.flatMap((x) => x.sourceIds))];
          return (
            <section
              className="note-section"
              data-section-id={section.id}
              key={section.id}
            >
              <Markdown value={section.markdown} {...common} reading />
              {!reading && ids.length > 0 && (
                <SourceCitation
                  groups={[{ id: section.id, transcriptIds: ids }]}
                  labels={labels}
                  details={details}
                  onSource={onSource}
                  onPlay={onPlay}
                />
              )}
              {!reading && (
                <small className="section-coverage">
                  {section.points.length} traceable points · section v
                  {section.version}
                  {section.userEdited ? " · protected user edits" : ""}
                </small>
              )}
            </section>
          );
        })
      ) : (
        <Markdown value={markdown} {...common} reading={reading} />
      )}
    </div>
  );
}
