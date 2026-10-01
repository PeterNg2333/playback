export function audioSourceLabel(sourceId: string) {
  if (sourceId === "microphone") return "Microphone";
  if (sourceId === "system") return "System audio";
  return sourceId;
}

export function AudioSourceBadge({ sourceId }: { sourceId: string }) {
  return (
    <span className="source-tag">
      <span className="source-avatar" data-source={sourceId} aria-hidden="true">
        {audioSourceLabel(sourceId).slice(0, 1).toUpperCase()}
      </span>
      <strong>{audioSourceLabel(sourceId)}</strong>
    </span>
  );
}
