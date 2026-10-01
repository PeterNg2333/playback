import clsx from "clsx";

// A level of this size or louder fills a bar.
const FULL_BAR_LEVEL = 250;
const SILENT_BAR_PX = 3;
const BAR_RANGE_PX = 18;

// Recent input level while recording, one bar per status poll; bars stay flat until speech is detected.
// Faint while no speech is detected, grey while paused; phones show every other bar.
export function LevelMeter({
  levels,
  speaking,
  paused,
}: {
  levels: number[];
  speaking: boolean;
  paused: boolean;
}) {
  const signal = paused ? "paused" : speaking ? "received" : "quiet";
  return (
    <span
      className="flex h-6.25 w-7.5 items-center justify-center gap-px md:w-15.5 md:gap-0.5"
      role="img"
      aria-label={`Audio signal ${signal}`}
    >
      {levels.map((level, index) => (
        <i
          key={index}
          className={clsx(
            "block min-h-0.75 w-0.5 rounded-[3px] transition-[height] duration-350 ease-[ease] max-md:even:hidden motion-reduce:transition-none",
            paused ? "bg-muted" : "bg-[#b44977]",
            !speaking && "opacity-35",
          )}
          style={{
            height: `${SILENT_BAR_PX + Math.round(Math.min(1, level / FULL_BAR_LEVEL) * BAR_RANGE_PX)}px`,
          }}
        />
      ))}
    </span>
  );
}
