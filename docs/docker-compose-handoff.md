# Prompt for the Docker Compose session

Copy the following into a new session in this repository:

```text
Implement a reliable local Docker Compose stack for Playback in app-temp/: MongoDB, the .NET 10 API, and the React/Vite UI. The stack must stay available after I close the terminal, with one documented command to start it and a browser URL that serves both the UI and /api.

Read AGENTS.md, docs/business-requirements.md, app-temp/README.zh-HK.md, app-temp/STRUCTURE.md, app-temp/docs/ai-flow.zh-HK.md, app-temp/compose.yaml, dev.mjs, and the current capture/storage/API code first. Inspect Git status and preserve all existing WIP, including the notes/chat UI changes from another session. Do not commit or push.

The existing Compose file has only MongoDB and a persistent playback_mongo volume. Identify and reuse the existing volume/database and recordings paths; do not silently create an empty replacement database. Do not clear, migrate destructively, delete recordings or run compose down -v. If destructive work is necessary, report the exact target, lost data and rebuild plan, then obtain my explicit approval.

Critical: WindowsAudioCaptureService uses Windows WASAPI for microphone and system loopback. Do not claim a Linux API container can capture Windows host audio. Implement a coherent host capture companion/bridge while containerizing the API/UI/database, or explain and implement a practical alternative that retains actual microphone/system/both capture, pause/resume, durable local chunks and retry. Inspect the current capture endpoints and client before choosing the integration. Do not replace working recording with mock success or just disable it without addressing the requirement.

Add Dockerfiles, an appropriately scoped .dockerignore, Compose services, persistent audio storage, health checks/readiness, restart policies and internal service networking. Ensure /api proxying works from the container UI; container localhost is not the host API or MongoDB. Bind public ports to host loopback for this local prototype. Make storage paths explicit/configurable rather than dependent on build-output directory depth. Keep the browser UI and chat usable when I close the startup terminal; document Docker Desktop and any host capture companion requirements.

Never inspect, print or modify .env contents. The runtime may load .env or process environment without exposing secrets. Exclude secrets, .git, local validation data, recordings and build artifacts from image contexts; never bake provider credentials into images or the frontend. Preserve the configured ASR, Gemini/Agent Framework, Jev, language settings, saved notes and source-backed chat workflows.

Check current Docker/.NET/database guidance against official documentation. I authorize the network downloads, image builds and container starts needed to implement and verify this local stack. Do not start recording or run paid provider/audio-upload tests without a separate live-test request. Inspect the target database and pending background work before starting services so verification does not inadvertently upload old audio or trigger paid AI work.

Verify using offline checks and an isolated test session: readiness, UI loading, session persistence across restart, saved notes and citations, chat's honest backend/provider error states, audio file persistence, and recovery after stopping/restarting the API. Check actual recording through the host bridge only when I authorize that live test. Capture relevant desktop/mobile UI screenshots and inspect them. Document exact start/stop/rebuild commands, evidence, limitations and unresolved capture checks. Deliver working files and clear results, not just a plan.
```
