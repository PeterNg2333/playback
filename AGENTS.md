# Playback agent guide

## Scope and direction

- The product aims to support roughly three-hour lectures and meetings with live transcription, AI notes, and source-backed questions. See [business requirements](docs/business-requirements.md) for required behavior and [scope decisions](docs/scope-decision.zh-HK.md) for project direction.
- `scripts/` contains feasibility studies and beginner material; `app-temp/` is a local prototype. Neither is a deployable product. Work within the area the user requests.
- The Week 3 sample is stored in this repository at `app-temp/data/test-audio/sampleAudio/`. It contains `sampleAudio.m4a` (the first 20 minutes), `transcript.txt` (the full lecture), and `Tutorial.txt`. Use this repository copy for future tests.
- Current technical direction: .NET 10/C#, Microsoft Agent Framework (not Semantic Kernel), Gemini Flash/Flash Lite, and a SenseVoice adapter. PostgreSQL under `scripts/` is a learning study; `app-temp/` currently uses local MongoDB. Frontend styling: Tailwind CSS v4 utilities; Markdown content in `markdown.css`.

## Development and acceptance

- Judge changes by business requirements and observable behavior. The UI, backend, internal APIs, and data model may be redesigned if required functionality still works. Prefer a coherent restructuring over layers of patches. Update relevant documentation and behavioral checks when changing them.
- Before editing, read relevant READMEs, code, and tests. Preserve uncommitted work. Verify meaningful outcomes and surface real failures instead of replacing them with mock success.
- Check changing .NET, Agent Framework, Gemini, Docker, and database guidance against official documentation.

## Data and operations

- The local development database may be cleared, rebuilt, or restructured destructively. **Before doing so, identify the target database, data that will be lost, and rebuild plan, then obtain the user's explicit approval.** After approval, a full restructuring is allowed. Do not affect other databases or user files.
- Do not commit, push, or delete user files without an explicit request. Do not inspect, modify, or commit `.env` contents. The application and test commands may load `.env` into their process environment; never display its contents in the agent conversation.
- Default study commands must be offline and reproducible. Network requests, software installation, starting containers, and audio uploads require explicit opt-in for the task. A user's request to run a live test is the opt-in for that test's network and audio upload steps. Do not add a separate lecturer, classmate, institution, privacy, or retention confirmation step to prototype live tests.
- Read secrets only from the process environment, including values loaded there by the application from `.env`. Never write them to code, documentation, logs, Git, or the agent conversation.
- Use fixed endpoints or allowlists, timeouts, response-size limits, and redirect safeguards for external requests. Identify audio chunks consistently by session, source, sequence, and hash so retries and recovery are idempotent.

For setup and current limitations, see `scripts/README.zh-HK.md` and `app-temp/README.zh-HK.md`.
