# Note model comparison — 18 October 2026

Question: can an open-weight model on OpenRouter write Playback's live notes as well as Gemini Flash-Lite, more cheaply? Five configurations wrote notes for the same speech through the production `NoteWorkflow`. The first round (prompt `tree-notes-v9`) showed that the main blocker was our note update contract, not the provider: every model hit it. The contract was then rebuilt as editor-style edit operations (`tree-notes-v10`, [below](#after-the-edit-operation-contract-tree-notes-v10)). That removed every review stop, and Gemini 3.5 Flash-Lite became the best candidate. The remaining blocker is that models defer too much speech.

## Setup

- Input: the repository Week 3 transcript (`app-temp/data/test-audio/sampleAudio/transcript.txt`), first 20 minutes — the span the sample audio covers — as 58 paired text sources. **The lecture is in English**, so this tests Traditional Chinese notes written from English speech; it says nothing about Cantonese input.
- Pacing: sources arrive two minutes at a time and each step makes at most one note call, as live notes would. Prompt `tree-notes-v9`; output `zh-Hant`; in-memory store; no Jev gate, MongoDB, audio upload or ASR.
- OpenRouter calls require every sent parameter (`require_parameters`, so structured outputs are guaranteed), skip 4-bit quantizations, and send the same output caps and constrained section schema as Gemini (`ModelOutputContract`).
- Runner: `pnpm.cmd test:note-models-live` (`app-temp/checks/Validation/run-note-models.mjs`). It reuses saved results unless given `--refresh`; `--models` picks candidates, for example `openrouter:deepseek/deepseek-v4.1-flash@none`. Evidence, including the generated notes, is kept locally under `app-temp/data/validation/runs/2026-10-18-note-models*/` and is not committed.

## Results

| Configuration | Calls: wrote notes / sent to review / failed / total | Sources cited | Median (max) latency per call | Cost for 20 min |
| --- | --- | --- | --- | --- |
| Gemini 3.5 Flash-Lite (Vertex) | 1 / 7 / 0 / 9 | 5 / 58 | 4.1 s (10.6 s) | $0.044 (estimate) |
| Gemini 3.1 Flash-Lite (Vertex, current default) | 5 / 3 / 1 / 10 | 34 / 58 | 2.8 s (4.1 s) | $0.048 (estimate) |
| DeepSeek V4.1 Flash, reasoning low | 7 / 0 / 1 / 9 | 58 / 58, but 9 only through placeholder points | 35.7 s (65.0 s) | $0.110 |
| DeepSeek V4.1 Flash, reasoning off | 5 / 3 / 2 / 10 | 51 / 58 | 6.6 s (29.2 s) | $0.040 |
| GLM-5.3-Flash, reasoning low | 1 / 2 / 5 / 8 (stopped after 3 failures in a row) | 5 / 51 | 9.7 s (31.5 s) | $0.019 |

"Sent to review" means the workflow saved a rewrite preview and deferred the batch's sources instead of writing them. That is the correct safety behaviour, but in live use those sources stay unwritten until a person reviews the preview. Gemini costs are reported tokens × Vertex list price; OpenRouter costs are the `usage.cost` it reports. A configuration that stalled spent fewer tokens, so its cost understates what a working run would cost. Total spend for all five runs: about US$0.26.

## Findings

1. **The update contract stops every model.** When a reply updates an existing topic, `RewriteCandidates` requires every earlier block body to reappear verbatim; otherwise the batch becomes a rewrite preview. Models routinely send only the new points for a topic they want to extend. This alone blocked 7 of 9 Gemini 3.5 batches and 3 of 10 batches for both Gemini 3.1 and DeepSeek without reasoning. Other contract rejections: `Unknown topic`, `Unknown retained point`, unknown source or term references, and an empty reply. `tree-notes-v9` had not been tested live with any model before this run (Phase 7 lists natural note quality as untested).
2. **Validation accepts empty content.** DeepSeek (reasoning low) saved a topic whose ten points were `...`, `mermaid...` and `table...` followed by a citation. The coverage audit counts those 9 sources as referenced. It also saved an empty topic whose title duplicates an existing subtopic. Gemini 3.1 and DeepSeek without reasoning repeated topic titles (1 and 3 times).
3. **Quality differs clearly.**
   - **DeepSeek** is the most faithful: it keeps unclear ASR words as quoted, labels guesses, and uses tables and diagrams where they help. It is also very long: 13.4k characters for 20 minutes, roughly 120k for a three-hour lecture. Its topic order did not follow the lecture.
   - **Gemini 3.1** is fluent but short: 1.4k characters, with 17 sources deferred. It silently "corrects" unclear speech into confident API and server names, which conflicts with the requirement to keep uncertainty visible.
   - **Gemini 3.5** and **GLM** wrote accurate, readable text in the batches that succeeded, but too few batches succeeded to judge them.
   - All outputs used Traditional characters (zero common Simplified-only characters found).
4. **Speed versus reliability for DeepSeek.** With low reasoning, DeepSeek was the only configuration that never needed review, but its median call took 36 s. With reasoning off it took 7 s, but it broke the contract as often as Gemini. Because OpenRouter spread calls across 4–5 serving providers, per-call latency varied widely.

## After the edit-operation contract (tree-notes-v10)

Round 2. (Round 3 below replaced Jev's edit approval with deterministic rules.) A reply is now a list of operations applied one by one, like an editor applying edits (see `app-temp/STRUCTURE.md`, "Note edits"):

- New topics, added blocks and moves apply at once.
- `replace-text` and `delete-block` apply when Jev judges them safe; otherwise they become suggestions.
- More than two deletions in one reply, or a deletion that removes a source's last citation, always waits for a person.
- One refused operation no longer blocks the rest.
- A topic returned with only new points is treated as an append, not a rewrite.
- A suggestion stays valid until its own block changes.

Getting real replies through needed four further fixes, each found from raw replies the harness now saves:

1. Vertex rejects `maxItems` on the `operations` array (`Request contains an invalid argument`, found by probing schema variants). The limit of 24 stays enforced in code.
2. Models name new topics without the suggested `new:` prefix, or put a body into `create-topic`. Any short alias is now accepted, and topic content is split into a block.
3. Gemini writes source citations as `[ref:T001]`. When every ID is a source alias in the input, it is now read as a source citation; an unknown term reference is still refused.
4. Gemini pasted an existing `cite_*` marker into new blocks as a generic citation, attributing unrelated claims to the lecture's first source. New blocks now cite only through `sourceIds`, and pasted markers are removed.

| Run | Configuration | Calls: wrote or completed / failed / total | Sources cited | Deferred by the model | Median call | Cost for 20 min |
| --- | --- | --- | --- | --- | --- | --- |
| v10b | Gemini 3.5 Flash-Lite | 9 / 0 / 9 | 58 / 58 | 0 | 3.3 s | $0.065 |
| v10c | Gemini 3.5 Flash-Lite (after fix 4) | 8 / 1 / 9 | 30 / 58 | 28 | 2.6 s | $0.055 |
| v10b | Gemini 3.1 Flash-Lite | 10 / 0 / 10 | 26 / 58 | 32 | 3.1 s | $0.040 |
| v10c | Gemini 3.1 Flash-Lite (after fix 4) | 9 / 0 / 9 | 36 / 58 | 22 | 2.7 s | $0.045 |
| v10b | DeepSeek V4.1 Flash, reasoning low | 9 / 0 / 9 | 5 / 58 | 0 (53 pending) | 33.6 s | $0.069 |
| v10b | DeepSeek V4.1 Flash, reasoning off | 9 / 1 / 10 | 12 / 58 | 46 | 5.1 s | $0.035 |

No configuration was sent to review in any v10 run. No model used `replace-text` or `delete-block` on this fresh content, so Jev's edit judgment is verified only by the offline checks (`NoteEditChecks`), not live. All runs together, both rounds, cost about US$0.76.

Findings:

- **Review stops are gone.** The same Gemini 3.5 model that wrote 1 of 9 batches under v9 wrote all 9 in v10b.
- **Deferral is now the bottleneck, and it varies between runs.** Between v10b and v10c, Gemini 3.5 went from 0 to 28 deferred sources on the same input. Typical reasons are "waiting for a suitable technical topic" or "will be integrated later". Deferred sources beyond the eight carried as context are not offered again until a coverage repair, so they stay out of the notes. A deferral budget is needed: a source already deferred once must be written or explicitly marked unclear on its next turn.
- **DeepSeek V4.1 Flash is not usable with this schema.** After its first reply, several OpenRouter providers returned operations with missing titles and bodies and even a corrupted key (`operations": 2.0,`). With reasoning off, it deferred almost everything.
- **Gemini 3.5 quality** (v10b notes, read in full): it covers the whole span in fluent Traditional Chinese. However, topics are not in lecture order and mix minute 1 with minute 18. It also still reconstructs commands from broken ASR (for example `x nt!*`) and names one server two different ways. Fix 4 addresses the misattributed citations found in that run.

## Round 3: lecture-first input, Jev's value and owner actions (tree-notes-v11)

Changes:

- **Input layout.** The writer now reads the whole lecture so far first, in time order, with stable aliases; the notes and the sources to write come after it.
- **Deferral.** A source may be deferred once; the schema and the code both enforce it.
- **Edit approval.** Text edits are now approved by deterministic rules instead of Jev: small, fact-keeping edits apply; others become suggestions.
- **Topic order.** Topics are kept in lecture order after every AI change, and emptied topics are removed.
- **Prompt.** Speech-recognition reconstructions must carry "(?)", and a new topic should start when the subject changes.
- **Owner actions.** Owners can lock blocks, give instructions (for the whole notes, a topic, or a comment on one block) and dismiss suggestions.

Live scheduling replay (Gemini 3.5 Flash-Lite, speech arriving as spoken, 10-second ticks, Stop at 20 minutes):

| Pacing | Writer calls | Jev calls (allowed) | Sources cited | Cost for 20 min |
| --- | --- | --- | --- | --- |
| Jev gate with 30 s deadline | 30 | 40 (1) | 58 / 58 | $0.148 |
| Write every 10 s (no gate) | 57 | 0 | 58 / 58 | $0.259 |
| Fixed 30 s batches (gate always waits) | 31 | 38 (0) | 58 / 58 | $0.131 |
| Fixed 30 s + organizer, final prompt | 30 writer + 9 organizer | 0 used | 58 / 58 | $0.168 |

- **Jev does not save anything as configured.** It allowed 1 of 40 decisions; every other write came from the 30-second deadline. A fixed 30-second batch makes the same number of writer calls without Jev. Writing every 10 seconds costs about 75% more.
- **Deferral is solved.** All four live runs cited every source; earlier rounds left up to 28 sources deferred.
- **Prompt caching earns no discount yet.** Two identical 11,810-token prefixes sent to Vertex Express returned `cachedContentTokenCount` 0 for both 3.5 and 3.1 Flash-Lite. Google's docs list implicit caching for Gemini 3 Flash and the 2.5 models, and only explicit caching for 3.1 Flash-Lite. The lecture-first input still gives the writer context and order, but its full input is billed every call. A three-hour lecture would cost roughly US$1.5–2 in writer input until explicit caching or a lecture window is used.
- **Topic structure is still unreliable.** Without the organizer, Flash-Lite put every block into one topic despite the prompt. With the organizer, one run produced four topics and the final run merged everything back into one. Speech-recognition reconstructions now carry "(?)" (16–24 per run).

## Round 4: batching without Jev and structure rules (owner decisions)

The owner decided to replace the Jev note gate with fixed batches and to enforce structure in code:

- **Admission.** Jev is off by default: a batch is written 30 s after its first new speech, at a speech pause, or at Stop.
- **Closed topics.** A topic with six blocks, or one running for eight minutes of lecture, is closed. A block still aimed at it goes into a new subtopic, titled by the block's first phrase.
- **Organization.** It is triggered by structure, not time. A changed topic over six blocks gets subtopics: its existing subtopics are in scope, and up to 80 operations are allowed. A level over six topics is regrouped from the outline.
- **Renaming.** `rename-topic` lets the organizer fix titles.

Live replay (Gemini 3.5 Flash-Lite, default batching, organizer on):

| Run | Writer calls | Organizer calls (failed) | Topics, depth | Sources cited | Cost for 20 min |
| --- | --- | --- | --- | --- | --- |
| Refusing blocks for closed topics | 45 | 5 (3) | 5, two levels; one topic held 23 blocks | 58 / 58 | $0.238 |
| Redirecting into subtopics | 29 | 6 (3) | 14, three levels | 58 / 58 | $0.118 |
| + organizer scope and rename | 30 | 3 (0) | 12, three levels | 58 / 58 | $0.109 |

- Refusing blocks only added calls, because Flash-Lite kept aiming at the closed topic. Redirecting fixed that.
- Splitting a large topic failed at first because of the 24-operation limit; the organizer now has 80.
- The notes now have up to three levels and no organizer failures.
- Remaining quality issues: automatically opened subtopics keep sentence-like titles until the organizer next runs on that part, and some groupings are semantically odd (a system-call topic under a study-method topic). Natural note quality still needs human review.

## Final benchmark (committed state)

The same 20 minutes on the committed default flow: no Jev, 30-second batches, structural organizer. The two-minute "Revise with AI" run is included for comparison.

| Configuration | Writer calls (failed) | Organizer calls | Topics, deepest level | Sources cited | Median call | Cost for 20 min |
| --- | --- | --- | --- | --- | --- | --- |
| Gemini 3.5 Flash-Lite, 30 s batches + organizer | 30 (1) | 1 | 8, three levels | 58 / 58 | 2.0 s | $0.105 |
| Gemini 3.1 Flash-Lite, 30 s batches + organizer | 28 (0) | 4 | 11, three levels | 58 / 58 | 2.1 s | $0.090 |
| Gemini 3.5 Flash-Lite, two-minute batches, no organizer | 9 (0) | 0 | 3, two levels | 58 / 58 | 2.7 s | $0.037 |

Cost scales with the number of calls more than with input size: two-minute batches cost about a third of 30-second batches for the same speech. A longer batch is the cheapest lever, at the price of notes appearing later.

## Round 5: placement harness and Qwen3.8 Flash with cache (tree-notes-v13)

The owner asked the model to decide layer and title like an author building a document or slide deck, and to use a model with caching.

- **Placement harness.** A reply declares new topics in `outline` (key, noun-phrase title, parent) and puts every block in `blocks` under an open topic or a key. The schema offers only open topics. Parents are limited to topics with room for another level that were spoken in the last five minutes. The organizer uses the same outline and only moves, reorders and renames.
- **Required targets.** Every operation must name its topic and block. Qwen otherwise left out the destination of `move-block` and wrote it only in `reason`.
- **Tolerant organizer.** Operations outside the selected topics, and renames without a title, are dropped instead of failing the whole regrouping.
- **Caching.** Vertex Express returned no cached tokens for any Flash or Flash-Lite model. Qwen3.8 Flash on OpenRouter, pinned to the Alibaba provider, cached the lecture prefix automatically (about 40% of input tokens over a run).

Live replay, 30-second batches with the organizer on:

| Run | Writer calls (failed) | Organizer calls (failed) | Topics | Sources cited | Median call | Cost for 20 min |
| --- | --- | --- | --- | --- | --- | --- |
| Gemini 3.5 Flash-Lite, v12 placement | 30 (1) | 0 | 13 | 58 / 58 | 1.9 s | $0.101 |
| Qwen3.8 Flash, no reasoning, v12 | 31 (0) | 9 (9) | 11 | 58 / 58 | 11.6 s | $0.056 |
| Qwen3.8 Flash, no reasoning, organizer on outline keys | 30 (0) | 3 (0) | 12 | 58 / 58 | 10.1 s | $0.035 |
| Qwen3.8 Flash, no reasoning, required targets + recent parents | 28 (0) | 5 (2) | 17 | 58 / 58 | 9.8 s | $0.027 |
| Gemini 3.5 Flash-Lite, same v13 contract | 28 (0) | 6 (1) | 24 | 58 / 58 | 2.1 s | $0.119 |

- With the full v13 contract, Qwen produced four top-level parts matching the lecture: AI-assisted study, system calls, course arrangements, and kernel debugging. Gemini on the same contract still grouped kernel debugging under course arrangements, and Nt/Zw under a mathematics review.
- Qwen marked 45 reconstructed words with "(?)" (Gemini: 16) and wrote more explanation. It still let in three Simplified characters (for example 讲, 从) in Traditional notes.
- Qwen with low reasoning took about 90 s per call and did not finish the 20 minutes within the 25-minute run budget, so it is unsuitable for live notes.
- The two organizer failures in the last Qwen run were renames without a title; those are now dropped.
- Qwen's cost is about a quarter of Gemini's. Its weakness is latency: about 10 s per call, so a note appears about 40 s after speech instead of about 32 s.

Results: `app-temp/data/validation/runs/2026-10-18-qwen-placement`, `…-qwen-organize-v13` and `…-placement-v13b`.

## Decision and next step

- Notes are written and organized by Qwen3.8 Flash on OpenRouter without reasoning (`NoteWriters.Default`, or `PLAYBACK_NOTE_MODEL`), with 30-second batches, the placement harness and the structural organizer. Without an OpenRouter key, notes fall back to the Gemini model. Chat, translation and term explanations stay on Gemini.
- Next:
  - Simplified characters in Traditional notes are now converted before saving with OpenCC `s2hk`, limited to Simplified-only characters (see `OpenccNoteScript`). On both Qwen runs every change was correct (讲师→講師, 从→從, 预告→預告, 学习→學習, 寻找→尋找, 线索→線索). A Simplified word made only of characters that are also Traditional (示范) still passes.
  - Review grouping on a longer and a Cantonese lecture, then reuse the placement harness for a meeting profile.

Sources for prices and model behaviour: [OpenRouter models API](https://openrouter.ai/api/v1/models), [OpenRouter structured outputs](https://openrouter.ai/docs/guides/features/structured-outputs), [OpenRouter provider routing](https://openrouter.ai/docs/guides/routing/provider-selection), [OpenRouter reasoning tokens](https://openrouter.ai/docs/guides/best-practices/reasoning-tokens), [Gemini API pricing](https://ai.google.dev/gemini-api/docs/pricing).
