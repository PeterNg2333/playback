export function termSegments<T extends { term: string }>(
  text: string,
  terms: T[],
) {
  const matches: { start: number; end: number; term: T }[] = [];
  const lower = text.toLocaleLowerCase();
  for (const term of terms) {
    const word = term.term.toLocaleLowerCase();
    if (!word) continue;
    let from = 0;
    while (from < text.length && matches.length < 50) {
      const start = lower.indexOf(word, from);
      if (start < 0) break;
      const end = start + word.length;
      from = end;
      // English terms match whole words, while Chinese terms can appear without spaces.
      if (
        /[a-z0-9_]/i.test(word[0]) &&
        /[a-z0-9_]/i.test(text[start - 1] ?? "")
      )
        continue;
      if (/[a-z0-9_]/i.test(word.at(-1)!) && /[a-z0-9_]/i.test(text[end] ?? ""))
        continue;
      matches.push({ start, end, term });
    }
  }
  matches.sort((a, b) => a.start - b.start || b.end - a.end);
  const segments: { text: string; term?: T }[] = [];
  let cursor = 0;
  for (const match of matches) {
    if (match.start < cursor) continue;
    if (match.start > cursor)
      segments.push({ text: text.slice(cursor, match.start) });
    segments.push({
      text: text.slice(match.start, match.end),
      term: match.term,
    });
    cursor = match.end;
  }
  if (cursor < text.length) segments.push({ text: text.slice(cursor) });
  return segments;
}
