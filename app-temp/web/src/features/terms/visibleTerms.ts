import type { TermInsight } from "../../types/api";

// Everyday words Jev sometimes still selects. This belongs in the backend's highlight
// decision; until it moves there, this is the one place the frontend drops them.
const COMMON_WORDS = ["details", "detail", "okay", "information"];

// The saved term explanations to highlight: chosen by Jev, not an everyday word, and
// written in the session's notes language.
export function visibleTerms(
  insights: TermInsight[] = [],
  noteLanguage?: string,
) {
  return insights.filter(
    (insight) =>
      insight.highlight &&
      !COMMON_WORDS.includes(insight.term.toLowerCase()) &&
      (!insight.outputLanguage || insight.outputLanguage === noteLanguage),
  );
}
