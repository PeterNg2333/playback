import type { Session } from "../../lib/backend/schemas";

export function noteOutline(sections: NonNullable<Session["currentNote"]>["sections"] = []) {
  const result: { section: (typeof sections)[number]; number: string; depth: number }[] = [];
  const known = new Set(sections.map(x => x.id));
  const visited = new Set<string>();
  function visit(parent: string | null, prefix: string, depth: number) {
    if (depth > 3) return;
    sections!.map((section, index) => ({section, index})).filter(({section}) => (known.has(section.parentId ?? "") ? section.parentId : null) === parent)
      .sort((a, b) => a.section.order - b.section.order || a.section.id.localeCompare(b.section.id)).forEach(({section}, i) => {
        if (visited.has(section.id)) return;
        visited.add(section.id); const number = prefix + (i + 1);
        result.push({section, number, depth}); visit(section.id, number + ".", depth + 1);
      });
  }
  visit(null, "", 1); return result;
}

export function topicMarker(number: string, depth: number) {
  const index = Number(number.split(".").at(-1));
  if (depth === 1) return `${index})`;
  if (depth === 2) {
    let label = "", value = index;
    while (value > 0) { value--; label = String.fromCharCode(97 + value % 26) + label; value = Math.floor(value / 26); }
    return label + ")";
  }
  let value = index, label = "";
  for (const [amount, digit] of [[100, "c"], [90, "xc"], [50, "l"], [40, "xl"], [10, "x"], [9, "ix"], [5, "v"], [4, "iv"], [1, "i"]] as const)
    while (value >= amount) { label += digit; value -= amount; }
  return label + ")";
}
