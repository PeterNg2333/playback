// A level of this size or louder fills a bar.
const FULL_BAR_LEVEL = 250;
const SILENT_BAR_PX = 3;
const BAR_RANGE_PX = 18;

// Recent input level while recording, one bar per status poll; bars stay flat until speech is detected.
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
      className="capture-wave"
      data-active={speaking}
      role="img"
      aria-label={`Audio signal ${signal}`}
    >
      {levels.map((level, index) => (
        <i
          key={index}
          style={{
            height: `${SILENT_BAR_PX + Math.round(Math.min(1, level / FULL_BAR_LEVEL) * BAR_RANGE_PX)}px`,
          }}
        />
      ))}
    </span>
  );
}
