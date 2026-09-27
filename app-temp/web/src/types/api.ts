import { z } from "zod";

export const SessionTitleSchema = z.string().trim().min(1).max(120);
export const GroupNameSchema = z.string().trim().min(1).max(80);
export const QuestionSchema = z.string().trim().min(1).max(1000);

export const TranscriptSchema = z.object({
  id: z.string(),
  sourceId: z.string(),
  startMs: z.number(),
  endMs: z.number(),
  recordedAt: z.string().nullish(),
  original: z.string(),
  translation: z.string().nullish(),
  translationLanguage: z.string().nullish(),
  translationStatus: z.string().optional(),
  translationAttempts: z.number().optional(),
  translationError: z.string().nullish(),
  recognitionStatus: z.string().optional(),
  noteStatus: z.string().optional(),
  revision: z.string().nullish(),
  uncertain: z.boolean(),
});
export const MaterialSchema = z.object({
  id: z.string(),
  name: z.string(),
  text: z.string(),
});
export const TermCandidateSchema = z.object({
  text: z.string(),
  transcriptIds: z.array(z.string()),
  materialIds: z.array(z.string()),
});
export const NoteEditSchema = z.object({
  kind: z.enum(["insert", "remove"]),
  line: z.number(),
  text: z.string(),
  transcriptIds: z.array(z.string()),
  materialIds: z.array(z.string()),
});
export const TermInsightSchema = z.object({
  id: z.string(),
  term: z.string(),
  highlight: z.boolean(),
  transcriptIds: z.array(z.string()),
  materialIds: z.array(z.string()),
  explanation: z.string().nullish(),
  evidence: z.array(z.object({ url: z.string().url(), title: z.string() })),
});
export const ChunkSchema = z.object({
  id: z.string(),
  sourceId: z.string(),
  sequence: z.number(),
  startMs: z.number(),
  endMs: z.number(),
  status: z.string(),
  recordedAt: z.string().nullish(),
  error: z.string().nullish(),
  asrAttempts: z.number().optional(),
});
export const SessionSummarySchema = z.object({
  id: z.string(),
  title: z.string(),
  groupId: z.string().nullish(),
});
export const GroupSchema = z.object({ id: z.string(), name: z.string() });
export const SessionSchema = SessionSummarySchema.extend({
  createdAt: z.string(),
  noteMarkdown: z.string(),
  noteVersion: z.number(),
  translationEnabled: z.boolean(),
  translationLanguage: z.string(),
  materials: z.array(MaterialSchema),
  transcripts: z.array(TranscriptSchema),
  chunks: z.array(ChunkSchema),
  terms: z.array(TermCandidateSchema),
  termInsights: z.array(TermInsightSchema).optional(),
  currentNote: z
    .object({
      author: z.string(),
      basedOnVersion: z.number().nullish(),
      inputHash: z.string().nullish(),
      transcriptIds: z.array(z.string()),
      materialIds: z.array(z.string()),
      sourceFrom: z.string().nullish(),
      sourceThrough: z.string().nullish(),
      inputTranscriptIds: z.array(z.string()).optional(),
      inputMaterialIds: z.array(z.string()).optional(),
      edits: z.array(NoteEditSchema).optional(),
    })
    .nullish(),
});
export const EvidenceSchema = z.object({
  kind: z.string(),
  id: z.string().nullish(),
  url: z.string().url().nullish(),
  title: z.string().nullish(),
  label: z.string().nullish(),
  startIndex: z.number().nullish(),
  endIndex: z.number().nullish(),
});
export const AnswerSchema = z.object({
  answer: z.string(),
  webAnswer: z.string().nullish(),
  webSuggestions: z.string().nullish(),
  evidence: z.array(EvidenceSchema),
  inference: z.boolean(),
});
export const HealthSchema = z.object({
  mongo: z.boolean(),
  gemini: z.boolean(),
  jev: z.boolean(),
  automaticAsr: z.boolean().optional(),
  asrPaused: z.boolean().optional(),
  sessionAudioMix: z.boolean().optional(),
});
export const CaptureStatusSchema = z.object({
  state: z.enum(["idle", "recording", "paused"]),
  sessionId: z.string().nullish(),
  bytes: z.record(z.string(), z.number()),
  autoAsr: z.boolean().optional(),
  noSoundWarning: z.boolean().optional(),
  error: z.string().nullish(),
  levels: z.record(z.string(), z.number()).optional(),
  capturedThroughMs: z.number().optional(),
  lastFinalizedAtMs: z.number().optional(),
  activeSegments: z.array(z.object({
    sourceId: z.string(),
    startMs: z.number(),
    endMs: z.number(),
    recordedAt: z.string(),
    streaming: z.boolean().optional(),
  })).optional(),
});

export type Transcript = z.infer<typeof TranscriptSchema>;
export type Material = z.infer<typeof MaterialSchema>;
export type TermCandidate = z.infer<typeof TermCandidateSchema>;
export type TermInsight = z.infer<typeof TermInsightSchema>;
export type Chunk = z.infer<typeof ChunkSchema>;
export type SessionSummary = z.infer<typeof SessionSummarySchema>;
export type Group = z.infer<typeof GroupSchema>;
export type Session = z.infer<typeof SessionSchema>;
export type Evidence = z.infer<typeof EvidenceSchema>;
export type Answer = z.infer<typeof AnswerSchema>;
export type Health = z.infer<typeof HealthSchema>;
export type CaptureStatus = z.infer<typeof CaptureStatusSchema>;
