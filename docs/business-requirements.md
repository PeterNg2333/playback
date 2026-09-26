# Playback business requirements

## Purpose and scope

Help students and meeting participants retain roughly three hours of audio, follow live transcripts and notes, and verify what was said afterward. These are product outcomes, not a prescribed UI layout, backend architecture, or database schema. They are **targets, not claims that the current prototype has implemented or verified them**. See [prototype status](../app-temp/README.zh-HK.md) for current limitations.

## Required behavior

| ID | User outcome | Acceptance criterion |
|---|---|---|
| BR-01 | Reliable recording | A local client saves audio in chunks before processing or upload. Network failure or a page reload does not lose completed chunks. Chunks can be retried and deduplicated. The target duration is about three hours. |
| BR-02 | Appropriate audio sources | Desktop capture covers all system playback audio, with the microphone as a separate source. Source and time information are retained. Mobile initially provides a viewing UI; future long mobile recordings require native capture and a local queue. |
| BR-03 | Verifiable transcript | ASR produces original text linked to audio time. Unclear speech is marked uncertain. Translations, corrections, and their versions are stored separately and never silently replace the original. |
| BR-04 | Continuously revised notes | Processed transcripts and materials from the same session feed periodic rolling notes. Note versions and traceable source locations are retained. |
| BR-05 | Terms and explanations | Candidate terms come from materials and confirmed transcripts. Explanations cite their sources. The system does not infer whether an individual student understands a term from the recording. |
| BR-06 | Evidence-backed questions | Users can ask about processed session content. Answers link to material pages or audio times. Online evidence includes verifiable links, and source content is distinguished from model inference. |
| BR-07 | Session and personal privacy | Materials, audio, transcripts, and notes stay within their session. Shared content is visible only to authorized users; personal AI questions are private by default. Recordings from multiple clients are aligned and deduplicated by source, time, and overlap. |
| BR-08 | Control and visibility | The UI lets users control local recording, see processing status, transcripts, notes, and sources, and ask questions. Navigation or page reload does not stop the local recording process. |

## Privacy and data handling

- Before recording, sharing, or sending material to a third party, confirm the lecturer's, classmates', and institution's consent and the applicable privacy and retention rules.
- Original audio and ASR text remain available for verification. Model guesses must not silently replace uncertain content.
- A local development database can be cleared or destructively restructured after the user's explicit approval. This is not the product's data retention policy.

## Change criterion

The UI, backend, internal API structure, and data model may be rebuilt, including removal of old implementation that obstructs a simpler design. Accept changes based on the relevant user flows, data correctness, and behavioral checks. If a business function or retention promise changes, update this document and explain the impact.
