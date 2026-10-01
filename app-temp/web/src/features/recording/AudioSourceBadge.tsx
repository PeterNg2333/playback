import clsx from "clsx";

export function audioSourceLabel(sourceId: string) {
  if (sourceId === "microphone") return "Microphone";
  if (sourceId === "system") return "System audio";
  return sourceId;
}

// The source an audio part came from: an initial in a round badge (accent for the
// microphone, blue for system audio) and its name. `small` shrinks the badge for lists.
export function AudioSourceBadge({
  sourceId,
  small = false,
}: {
  sourceId: string;
  small?: boolean;
}) {
  return (
    <span className="inline-flex items-center gap-1.5 text-ink">
      <span
        className={clsx(
          "grid flex-none place-items-center rounded-full text-[10px] font-[750] text-white",
          small ? "size-4.5" : "size-5.5",
          sourceId === "system" ? "bg-blue" : "bg-accent",
        )}
        aria-hidden="true"
      >
        {audioSourceLabel(sourceId).slice(0, 1).toUpperCase()}
      </span>
      <strong className="text-[11px]">{audioSourceLabel(sourceId)}</strong>
    </span>
  );
}
