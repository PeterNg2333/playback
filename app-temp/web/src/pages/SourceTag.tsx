export function sourceLabel(sourceId: string) {
  if (sourceId === "microphone") return "Microphone";
  if (sourceId === "system") return "System audio";
  return sourceId;
}

export function SourceTag({ sourceId }: { sourceId: string }) {
  return (
    <span className="source-tag">
      <span className="source-avatar" data-source={sourceId} aria-hidden="true">
        {sourceId === "microphone" ? "M" : sourceId === "system" ? "S" : sourceId.slice(0, 1).toUpperCase()}
      </span>
      <strong>{sourceLabel(sourceId)}</strong>
    </span>
  );
}
