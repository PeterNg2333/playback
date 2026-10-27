// Deploys the root Dockerfile to Cloud Run (network, billable): the image is built by Cloud Build, recordings, keys and
// prompts live on a private bucket mounted at /data, and secret values go from this process's environment (.env loaded by
// `npm run deploy:cloud-run`) into Secret Manager without being printed. Re-running builds a new image and adds a secret
// version only when a value changed. Creating the service also mounts the bucket; later runs keep the mount as it is.
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const project = "peterngdev";
const region = "asia-east1";
const service = "playback";
const bucket = `${project}-playback-data`;
const account = `playback-run@${project}.iam.gserviceaccount.com`;
const image = `${region}-docker.pkg.dev/${project}/playback/playback:${new Date().toISOString().replace(/\D/g, "").slice(0, 14)}`;
const required = ["PLAYBACK_MONGO_URI", "PLAYBACK_DEMO_PASSWORD"];
const optional = ["GOOGLE_AI_STUDIO_API_KEY", "OPENROUTER_API_KEY", "JEV_API_KEY"];

// Arguments are simple words; Windows runs gcloud.cmd through the shell, which joins them with spaces.
function gcloud(args, { input, show = false, allowFailure = false } = {}) {
  const result = spawnSync("gcloud", [...args, `--project=${project}`, "--quiet"], {
    cwd: root,
    input,
    encoding: "utf8",
    shell: process.platform === "win32",
    stdio: [input === undefined ? "ignore" : "pipe", show ? "inherit" : "pipe", show ? "inherit" : "pipe"],
  });
  if (result.status !== 0 && !allowFailure)
    throw new Error(`gcloud ${args.slice(0, 3).join(" ")} failed${show ? "" : ":\n" + result.stderr}`);
  return { ok: result.status === 0, out: (result.stdout ?? "").trim() };
}

// A new service account takes a few seconds to appear to IAM.
function grant(args) {
  for (let attempt = 1; ; attempt++) {
    if (gcloud(args, { allowFailure: attempt < 6 }).ok) return;
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 10_000);
  }
}

function ensure(label, describe, create) {
  if (gcloud(describe, { allowFailure: true }).ok) return console.log(`${label}: exists`);
  gcloud(create);
  console.log(`${label}: created`);
}

for (const name of required)
  if (!process.env[name]) throw new Error(`${name} is empty; set it in .env`);
if (/\/\/(?:[^@/]*@)?(?:127\.0\.0\.1|localhost)[:/]/.test(process.env.PLAYBACK_MONGO_URI))
  throw new Error("PLAYBACK_MONGO_URI points at this computer; Cloud Run needs MongoDB Atlas or Firestore");
const upload = gcloud(["meta", "list-files-for-upload", "."]).out.split(/\r?\n/);
if (upload.some((file) => /(^|[\\/])\.env(\.|$)/.test(file)))
  throw new Error(".gcloudignore would upload an .env file");

ensure("Image repository", ["artifacts", "repositories", "describe", "playback", `--location=${region}`],
  ["artifacts", "repositories", "create", "playback", "--repository-format=docker", `--location=${region}`]);
ensure("Service account", ["iam", "service-accounts", "describe", account],
  ["iam", "service-accounts", "create", "playback-run", "--display-name=Playback-Cloud-Run"]);
// Soft delete stays on: the database names recordings and prompts only by hash, so a deleted object cannot be rebuilt.
ensure("Bucket", ["storage", "buckets", "describe", `gs://${bucket}`],
  ["storage", "buckets", "create", `gs://${bucket}`, `--location=${region}`, "--uniform-bucket-level-access",
    "--public-access-prevention", "--soft-delete-duration=7d"]);
grant(["storage", "buckets", "add-iam-policy-binding", `gs://${bucket}`, `--member=serviceAccount:${account}`,
  "--role=roles/storage.objectUser"]);

const secrets = [];
for (const name of [...required, ...optional]) {
  const value = process.env[name];
  if (!value) { console.log(`${name}: empty, not set on the service`); continue; }
  // Prefixed so other services' secrets in the project (an existing openrouter-api-key) are never touched.
  const id = "playback-" + name.toLowerCase().replace(/^playback_/, "").replaceAll("_", "-");
  ensure(`Secret ${id}`, ["secrets", "describe", id], ["secrets", "create", id, "--replication-policy=automatic"]);
  const latest = gcloud(["secrets", "versions", "access", "latest", `--secret=${id}`], { allowFailure: true });
  const version = latest.ok && latest.out === value
    ? gcloud(["secrets", "versions", "describe", "latest", `--secret=${id}`, "--format=value(name)"]).out
    : gcloud(["secrets", "versions", "add", id, "--data-file=-", "--format=value(name)"], { input: value }).out;
  grant(["secrets", "add-iam-policy-binding", id, `--member=serviceAccount:${account}`,
    "--role=roles/secretmanager.secretAccessor"]);
  secrets.push(`${name}=${id}:${version.split("/").pop()}`);
}

console.log(`Building ${image} with Cloud Build...`);
gcloud(["builds", "submit", `--tag=${image}`, "--timeout=1800s"], { show: true });

const number = gcloud(["projects", "describe", project, "--format=value(projectNumber)"]).out;
const origin = `https://${service}-${number}.${region}.run.app`;
const env = [`PLAYBACK_PUBLIC_ORIGIN=${origin}`];
if (process.env.PLAYBACK_MONGO_DATABASE) env.push(`PLAYBACK_MONGO_DATABASE=${process.env.PLAYBACK_MONGO_DATABASE}`);
const exists = gcloud(["run", "services", "describe", service, `--region=${region}`], { allowFailure: true }).ok;
// Background workers need CPU between requests; one instance because queues and locks are in process memory.
gcloud(["run", "deploy", service, `--image=${image}`, `--region=${region}`, `--service-account=${account}`,
  "--allow-unauthenticated", "--no-cpu-throttling", "--min-instances=0", "--max-instances=1", "--cpu=1",
  "--memory=1Gi", "--timeout=300", `--update-env-vars=${env.join(",")}`, `--update-secrets=${secrets.join(",")}`,
  ...(exists ? [] : [`--add-volume=name=data,type=cloud-storage,bucket=${bucket},mount-options="uid=1654;gid=1654"`,
    "--add-volume-mount=volume=data,mount-path=/data"])], { show: true });

// Cloud Run's front end answers /healthz itself, so the app is probed through /api/health: 401 means it is serving.
const health = await fetch(`${origin}/api/health`, { redirect: "manual", signal: AbortSignal.timeout(60_000) });
console.log(`${origin}/api/health → HTTP ${health.status}${health.status === 401 ? " (running; sign in for details)" : ""}`);
console.log(`Open ${origin} and sign in with the demo password.`);
