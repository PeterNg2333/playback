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
docker run --name playback-demo --env-file .env --env PLAYBACK_PUBLIC_ORIGIN=http://localhost:8080 --publish 127.0.0.1:8080:8080 --mount type=volume,source=playback_demo_data,target=/data playback:cloud-run
```

Open `http://localhost:8080`. Set `PLAYBACK_MONGO_URI` to a reachable database;
`127.0.0.1` inside a container is the container itself. For a local host database,
Docker Desktop provides `host.docker.internal`. Audio metadata and its saved files
must belong to the same dataset: an empty new volume does not contain audio from
an existing Windows installation. Stop with `docker stop playback-demo`; retain
the named data volume for subsequent runs.

## Configuration

`.env.example` contains only six values: MongoDB connection, public origin, shared
password and three provider keys. Paste these into Cloud Run's Variables & Secrets.
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

`GET /healthz` checks process liveness; authenticated `/api/health` reports database
and provider readiness. `.env`, local recordings and build outputs are excluded
from the Docker build context.

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

The proposed cloud setup is **Cloud Run service + a private Cloud Storage bucket
for audio + MongoDB Atlas for workspace data**. Cloud Run's local disk is temporary.
For the current filesystem-based code, mount a writable bucket at `/data/audio`:

1. Create a private bucket in the service's region; keep public access blocked.
2. Grant the service account `roles/storage.objectUser` on that bucket.
3. Add a Cloud Storage bucket volume at `/data/audio`, writable, with mount options
   `uid=1654;gid=1654` to match the container user.
4. Keep `/data/keys` on separate persistent private storage if logins must survive
   instance replacement. Signing keys must not be stored in public storage.
5. Verify upload, immediate playback, seeking, retries, deletion and instance
   replacement against the real cloud mount before keeping real recordings.

Bucket mounts use Cloud Storage FUSE: they are not fully POSIX-compliant and have
no same-file write locking. The current temporary-file/move workflow passed on
local Docker but remains unverified on a bucket mount. A direct Cloud Storage API
integration would be a later code change. NFS/Filestore is another filesystem
option, with additional VPC/NFS setup; Cloud Run mounts NFS without file locking.
Keep one active instance/revision for the current in-process queues.

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

## Recording limitation

The existing recording controls use Windows WASAPI on the machine running the
API. A Linux Cloud Run container cannot access the user's microphone or system
audio; its UI displays “Recording unavailable”. This deployment supports the
existing workspace/API but does **not** add a
browser recorder or a local Windows capture bridge. Those require separate work.
The authenticated WAV chunk-upload API remains available for a compatible client;
this guide does not claim browser recording or three-hour cloud recording acceptance.

References: [Cloud Run container contract](https://docs.cloud.google.com/run/docs/container-contract),
[billing settings](https://docs.cloud.google.com/run/docs/configuring/billing-settings),
[ASP.NET Core cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0),
[antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0),
[Docker multi-stage builds](https://docs.docker.com/build/building/multi-stage/).
Storage references: [Cloud Storage mounts](https://docs.cloud.google.com/run/docs/configuring/services/cloud-storage-volume-mounts),
[NFS mounts](https://docs.cloud.google.com/run/docs/configuring/services/nfs-volume-mounts).
