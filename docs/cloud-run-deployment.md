# Playback: one container for the web UI and API

Deploy the root `Dockerfile` to a **Cloud Run service** using a container image.
The same .NET process serves the compiled React frontend and `/api`; no Vite or
separate frontend server runs in production. MongoDB stays external (for example,
MongoDB Atlas). Users choose their own username; first successful login creates it
in MongoDB. Each username has its own sessions, groups, notes, materials,
transcripts, audio and conversations. Demo users share one environment-controlled
password.

## Build and check locally

From the repository root, with Docker Desktop running:

```powershell
docker build --platform linux/amd64 -t playback:cloud-run .
node app-temp/web/test/docker-deployment.check.mjs
```

The build downloads official Node/.NET images and package dependencies. The check
starts isolated disposable app/MongoDB containers on a private network with no
external provider traffic; it uses synthetic credentials and no `.env`. It needs
the existing `mongo:8.0.32-noble` image and installed web dependencies (Playwright
Core and Edge on Windows). Override `PLAYBACK_BROWSER` for another installed
Chromium browser; `PLAYBACK_DOCKER_TEST_PORT` defaults to 8088. It verifies real
MongoDB writes, restart retention, login/logout, origin/CSRF rejection, production
route restrictions, WAV persistence/range playback/upload identity, browser rendering
and secure-cookie configuration, then removes
only the containers and network it created. Existing databases and volumes stay intact.

Local verification on 2026-10-05 passed on Docker Desktop (Linux AMD64) with a real
isolated MongoDB and Edge browser: login/CSRF/origin checks, session/note writes,
synthetic silent WAV upload/deduplication/range playback, retention after restart,
desktop/mobile UI, logout, rate limiting and HTTPS cookie flags. The existing
backend offline checks also passed. These results exclude live providers,
microphone capture, Cloud Run deployment and cloud volume integration.

The multi-user follow-up passed with automatic MongoDB username creation and two independent logins: private workspace lists
and notes, foreign-ID read/write denial (including note history, chats, sync, AI
activity, audio ranges and uploads), rejected forged ownership, hidden unassigned
legacy records, isolation after restart and account switching in a real browser.

For your own local run, populate your private `.env` yourself and pass it at runtime:

```powershell
docker run --name playback-demo --env-file .env --env ENVIRONMENT=dev --env PLAYBACK_PUBLIC_ORIGIN=http://localhost:8080 --publish 127.0.0.1:8080:8080 --mount type=volume,source=playback_demo_data,target=/data playback:cloud-run
```

Open `http://localhost:8080`. Set `PLAYBACK_MONGO_URI` to a reachable database;
`127.0.0.1` inside a container is the container itself, so `--env ENVIRONMENT=dev`
overrides a `.env` that says `local` (which would point the container at itself). For a local host database,
Docker Desktop provides `host.docker.internal`. Audio metadata and its saved files
must belong to the same dataset: an empty new volume does not contain audio from
an existing Windows installation. Stop with `docker stop playback-demo`; retain
the named data volume for subsequent runs.

## Configuration

`.env.example` contains seven values: `ENVIRONMENT`, MongoDB connection, public
origin, shared password and three provider keys. Paste all but `ENVIRONMENT` into
Cloud Run's Variables & Secrets; the image already sets `ENVIRONMENT=prod`, which
requires `PLAYBACK_MONGO_URI` instead of falling back to a local database.
`ENVIRONMENT=local` makes the app use the Docker MongoDB on `127.0.0.1:27017`
whatever the URI says; `dev`, `prod` or no value use `PLAYBACK_MONGO_URI`.
Cloud Run does not load a local `.env` automatically. Leave unused provider keys
empty. Use `demo123!` as the sample shared password.

There is no username list. Enter a username and the shared password on the login
page; a new username creates a MongoDB `users` record, while an existing username
opens its existing workspace. Usernames are case-sensitive, 1–64 characters using
letters, numbers, dots, underscores or dashes. Because the password is shared,
anyone who knows it can sign in as an existing username; this is demo separation,
not private authentication between people who share that password.

`app-temp/api/appsettings.json` sets `Storage:DataPath` to `/data` and
`Storage:KeyPath` to `/data/keys`. Development config uses the existing local data
folder. The database defaults to `playback_prototype`; session language and model
settings remain in MongoDB. The image sets Production and port 8080; Cloud Run
supplies `PORT` automatically. No path, account-list or local-HTTP env is needed.

`PLAYBACK_PUBLIC_ORIGIN` must be the exact HTTPS app origin without a trailing
slash. Local HTTP is accepted only for a loopback origin such as
`http://localhost:8080`. Production startup requires a password and origin.
Unconfigured native Development retains the existing Vite workflow.

`GET /healthz` checks process liveness locally; on Cloud Run's `run.app` URL the
front end answers that path itself (404), so probe `/api/health` there (401 before
sign-in). Authenticated `/api/health` reports database and provider readiness. `.env`, local recordings and build outputs are excluded
from the Docker build context.

## Scripted Cloud Run setup

`npm run deploy:cloud-run` runs [app-temp/deploy-cloud-run.mjs](../app-temp/deploy-cloud-run.mjs) with
the local gcloud login. It deploys project `peterngdev` in region `asia-east1`; edit the constants at the top of the
script to change either. The run is billable and network-bound, and it does the following:

- creates the Artifact Registry repository, the service account `playback-run`, and the private bucket
  `peterngdev-playback-data` (7-day soft delete) if they are missing;
- copies the five secret values from `.env` into Secret Manager without printing them, adding a version only when a
  value changed;
- builds the image with Cloud Build (`.gcloudignore` uploads only the Dockerfile's sources and refuses any `.env`);
- deploys the service: public ingress, instance-based billing, 0 to 1 instances, 1 vCPU and 1 GiB memory, 300 s
  timeout, with the bucket mounted at `/data`;
- sets `PLAYBACK_PUBLIC_ORIGIN` to the service's `https://playback-<project number>.asia-east1.run.app` URL.

MongoDB Atlas must accept Cloud Run's changing outbound addresses, for example a `0.0.0.0/0` network access entry
protected by the database password and TLS.

## Manual Cloud Run setup

1. Tag and push `playback:cloud-run` to your chosen Artifact Registry repository,
   then select that image when creating a Cloud Run **service**.
2. Permit public access at Cloud Run's ingress so browsers can reach the app's
   login page. The app itself requires a signed-in demo user for its UI/API.
   Set the exact service URL as `PLAYBACK_PUBLIC_ORIGIN`; custom domains require
   their exact origin instead. Other origins are rejected.
3. Choose instance-based billing (CPU outside requests), minimum instances **1**,
   maximum instances **1**, and keep traffic on one active revision. Current AI
   queues/locks are in process memory and background workers need CPU between
   requests. This limits scaling; it does not guarantee task completion across
   restarts. Drain work before replacing a revision.
4. Use an external MongoDB and a **persistent writable volume** for audio and
   keys before keeping real recordings. The container runs as UID/GID **1654**;
   mounted directories must be writable by that identity. A persistent filesystem
   mounted at `/data` can hold both; a separate `/data/audio` mount still needs
   persistent `/data/keys`. Restrict access to signing keys. Cloud Storage mounts
   are a possible audio backing store, but their filesystem behavior and this
   app's upload/playback/delete paths have not been verified here; do not assume
   a bucket mount has passed those checks.
5. Start with at least 1 GiB memory and a request timeout of 300 seconds for the
   demo; adjust from observed workload. Test database connectivity after login.
   Never enable `PLAYBACK_OFFLINE_TEST` or test-data settings in the deployed app.

Cloud Run's default local filesystem is ephemeral. Without persistent audio
storage, MongoDB can retain a transcript while its recording disappears. Without
persistent signing keys, replacing an instance signs users out. The local Docker
check verifies a restart of the same container, not Cloud Run replacement or a
Cloud Storage volume. No cloud deployment, live AI call or real audio upload is
part of this implementation check.

## Audio storage

Completed WAVs are files. Local Windows storage defaults to
`app-temp/data/audio/<sessionId>/<sourceId>/<sequence>-<sha256>.wav`;
Docker uses `/data/audio/<sessionId>/<sourceId>/<sequence>-<sha256>.wav`.
MongoDB stores chunk metadata and session ownership, along with notes, transcripts,
materials and chats. Windows capture also uses `data/local-capture` while sealing
and importing WAVs. The image does not include existing local recordings.

The proposed cloud setup is **Cloud Run service + one private Cloud Storage bucket
mounted at `/data` + MongoDB Atlas or Firestore for workspace data**. Cloud Run's
local disk is temporary. The bucket holds the recordings (`/data/audio`), the
login signing keys (`/data/keys`) and AI activity instructions (`/data/prompts`,
one write-once file per SHA-256, so `ai_activity` documents keep only the hash):

1. Create a private bucket in the service's region; keep public access blocked.
   Signing keys live in it, so it must never be made public. Keep soft delete on
   (or turn on Object Versioning): the database names recordings and prompts only
   by hash, so a deleted object cannot be rebuilt from MongoDB.
2. Grant the service account `roles/storage.objectUser` on that bucket.
3. Add a Cloud Storage bucket volume at `/data`, writable, with mount options
   `uid=1654;gid=1654` to match the container user.
4. Verify upload, immediate playback, seeking, retries, deletion, a prompt shown in
   the AI flow and login after instance replacement against the real cloud mount
   before keeping real recordings.

With `ENVIRONMENT=prod` (the image default) a chunk whose ASR and second pass are
done is kept as 24 kb/s Opus, about a tenth of its WAV (a 3-hour source ≈ 35 MB
instead of 345 MB), and decoded to a temporary WAV (at most 128 MB, in Cloud
Run's memory-backed `/tmp`) when played, rerun or rendered. The second ASR pass
and the first run on WAV; reruns of compressed audio hear the Opus version.
AI activity documents expire after 30 days (TTL index `activity_expiry`; Firestore
deletes within a day of expiry, billed as normal deletes); lecture data does not
expire. A prompt is read from the bucket at most once per instance: saved and read texts
stay in memory by hash, and the misses of one activity request are read in parallel.

Bucket mounts use Cloud Storage FUSE: they are not fully POSIX-compliant and have
no same-file write locking. The current temporary-file/move workflow passed on
local Docker but remains unverified on a bucket mount. A direct Cloud Storage API
integration would be a later code change. NFS/Filestore is another filesystem
option, with additional VPC/NFS setup; Cloud Run mounts NFS without file locking.
Keep one active instance/revision for the current in-process queues.

The connection string goes in `PLAYBACK_MONGO_URI` (not `..._URL`). A remote
database gets 15 s for the first server selection, localhost 2 s.
`npm run test:mongo-storage-live` checks the storage format against that
database in `playback_e2e` (one synthetic session, deleted afterwards). On
2026-10-26 it passed against MongoDB Atlas: no null fields, a missing
`SupersededBy` matched as null, prompts filed and read back; per document
828 → 538 B for a transcript and 2,946 → 632 B for an activity with a notes
prompt. From Windows, the .NET driver's own `mongodb+srv` DNS lookup took up to
16 s while Node resolved it in under 0.1 s, so the run used a seed list
resolved by Node; check first-request latency on Cloud Run (Linux) after deploy.

### Firestore instead of Atlas (unverified)

Firestore Enterprise edition with MongoDB compatibility keeps the database in the
same Google Cloud project and avoids an Atlas IP access list. Its database IDs
allow only lowercase letters, numbers and hyphens, so create the database as
`playback-prototype` and add a seventh variable `PLAYBACK_MONGO_DATABASE=playback-prototype`.
Use the connection string from the console; it must keep `loadBalanced=true`,
`tls=true` and `retryWrites=false`. The C# driver 3.x is supported. The app has
not run against Firestore: it relies on MongoDB duplicate-key errors for
idempotent transcript, note-history and term writes, and Firestore documents that
error codes may differ. Check repeated uploads and note saves before keeping data.
Null fields are not written, so recovery queries such as active transcripts
(`SupersededBy: null`) rely on `{field: null}` matching a missing field, as MongoDB
does; Firestore's documentation does not say, so check that a new transcript is
listed and gets notes.
See [behavior differences](https://docs.cloud.google.com/firestore/mongodb-compatibility/docs/behavior-differences)
and [connection strings](https://docs.cloud.google.com/firestore/mongodb-compatibility/docs/connect).

## Login and request protection

Usernames are saved in MongoDB; only the shared password comes from the environment.
Successful login sets
an HttpOnly, SameSite=Strict session cookie with an eight-hour fixed lifetime.
Production cookies also have Secure; the configured HTTPS scheme accounts for
Cloud Run's HTTP hop after TLS termination without trusting forwarding headers.
Changing the shared password invalidates existing sessions on their next request.
Login attempts are capped at ten per minute per instance.

Every API endpoint, including audio downloads and answer streaming, requires the
session. Writes, login and logout require the configured `Origin` and a valid ASP.NET
antiforgery token. The frontend forwards the token in `X-CSRF-TOKEN`; its readable
`Playback.Csrf` cookie contains only that token, not the session or password.
Cross-origin requests and cross-site browser fetches are rejected; production
does not enable CORS. Unknown API routes return JSON 404, not the frontend HTML.
Origin checks protect browser use; non-browser clients can supply an Origin
header and still need valid credentials, cookies and tokens.

Session and group owners come from the authenticated cookie, never a submitted
`ownerId`. Lists include only that owner's records. Resource APIs check the owning
session/group before reads or writes, including audio and group-flow selection.
Foreign and missing resources both return 404. Uploads cannot attach audio to
another user's session.

Pre-existing unowned records remain intact but are hidden from secured accounts.
The original unsecured local Development mode retains its access. No database was
cleared or migrated. Assigning existing notes to a user requires a separately
scoped migration; no ownership transfer was performed here.

## Recording

A Linux Cloud Run container cannot use the Windows WASAPI recorder, so health
reports `localCapture: false` and the page records in the browser instead: the
microphone through the browser's permission, system audio by sharing a screen
with audio (Chrome/Edge on Windows: Entire screen and “Also share system audio”;
a shared tab gives that tab's audio; phone browsers record the microphone only).
The page keeps 16 kHz mono WAV chunks in IndexedDB and uploads them in order to
the authenticated `POST /api/chunks`, retrying after failures and reloads; it
opens, renews and closes their stream with `PUT /api/sessions/{id}/streams/{streamId}`,
and Stop asks for the after-lecture notes. Browser APIs need HTTPS, which Cloud
Run provides. Uploads go through the same origin and CSRF checks as other writes.

This was checked offline only (Edge's fake microphone, a page-made screen share,
fixture API). It has not been run against a deployed instance, real shared
system audio or a three-hour recording. Browser recording has no live interim
text. Cantonese display conversion (`LanguageSettings.CantoneseDisplay`) still
calls a Windows API and throws on Linux, so Cantonese sessions cannot be read in
the container until it is replaced.

References: [Cloud Run container contract](https://docs.cloud.google.com/run/docs/container-contract),
[billing settings](https://docs.cloud.google.com/run/docs/configuring/billing-settings),
[ASP.NET Core cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0),
[antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0),
[Docker multi-stage builds](https://docs.docker.com/build/building/multi-stage/).
Storage references: [Cloud Storage mounts](https://docs.cloud.google.com/run/docs/configuring/services/cloud-storage-volume-mounts),
[NFS mounts](https://docs.cloud.google.com/run/docs/configuring/services/nfs-volume-mounts).
