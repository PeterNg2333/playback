// The notes' headings as a collapsible, indented list; choosing one scrolls the notes to it.
export function TopicTree({
  topics,
  onSelect,
}: {
  topics: { title: string; level: number }[];
  onSelect: (index: number) => void;
}) {
  const top = Math.min(...topics.map((item) => item.level));
  return (
    <details
      className="mb-5.5 rounded-[10px] border border-line bg-accent-soft p-3.5"
      open
    >
      <summary className="cursor-pointer text-[12px] font-bold">
        Topic tree · {topics.length}
      </summary>
      <nav aria-label="Note topics">
        <ol className="mt-3">
          {topics.map((topic, index) => (
            <li
              className="border-l-2 border-line py-1 pl-3"
              key={`${index}-${topic.title}`}
              style={{ marginLeft: (topic.level - top) * 14 }}
            >
              <button
                type="button"
                className="cursor-pointer text-left text-[12px] text-accent"
                onClick={() => onSelect(index)}
              >
                {topic.title}
              </button>
            </li>
          ))}
        </ol>
      </nav>
    </details>
  );
}
