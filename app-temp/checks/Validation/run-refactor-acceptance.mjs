// Explicit opt-in; existing localhost Mongo only, fresh retained test data, isolated API/data root.
import {spawn} from 'node:child_process';
import {access, mkdir, writeFile} from 'node:fs/promises';
import {randomUUID} from 'node:crypto';
import path from 'node:path';
const root = process.cwd();
const options = new Set(process.argv.slice(2));
if (!options.has('--allow-local-mongo')) throw new Error('Use --allow-local-mongo only after approving the isolated local database test');
if ([...options].some(x => !['--allow-local-mongo', '--allow-live-sample'].includes(x))) throw new Error('Unknown acceptance option');
const live = options.has('--allow-live-sample');
process.env.PLAYBACK_REFACTOR_MONGO = 'yes';
await access(path.resolve(root, 'app-temp/web/dist/index.html'));
const children = new Set();
function start(args, config) {
  const child = spawn('dotnet', args, {...config, windowsHide: true});
  children.add(child);
  child.once('exit', () => children.delete(child));
  child.once('error', () => children.delete(child));
  return child;
}
for (const [signal, code] of [['SIGINT', 130], ['SIGTERM', 143]]) process.once(signal, () => {
  for (const child of children) child.kill();
  process.exit(code);
});
const run = path.resolve(root, 'output', 'refactor-acceptance', new Date().toISOString().replace(/[:.]/g, '-'));
await mkdir(run, {recursive: true});
const env = {...process.env, PLAYBACK_REFACTOR_MONGO: 'yes', PLAYBACK_MONGO_DATABASE: 'playback_e2e', PLAYBACK_OFFLINE_TEST: 'yes',
  Storage__DataPath: path.join(run, 'data'), Storage__KeyPath: path.join(run, 'keys'),
  PLAYBACK_AUTO_NOTES: 'no', PLAYBACK_AUTO_TERMS: 'no', PLAYBACK_AUTO_ORGANIZE: 'no', PLAYBACK_AUTO_TRANSLATION: 'no', PLAYBACK_RESUME_SAVED_ASR: 'no'};
delete env.PLAYBACK_DEMO_PASSWORD;
delete env.PLAYBACK_PUBLIC_ORIGIN;
// Keep remote/credentialed database settings out of this localhost-only acceptance host.
if (env.PLAYBACK_MONGO_URI && !/^mongodb:\/\/(?:127\.0\.0\.1|localhost):\d+\/?$/.test(env.PLAYBACK_MONGO_URI))
  throw new Error('Acceptance requires a single existing localhost Mongo endpoint without extra connection options');
const dll = name => path.resolve(root, 'app-temp', name, 'bin/verification/net10.0', `Playback.${name === 'api' ? 'Api' : 'Checks'}.dll`);
const origin = 'http://127.0.0.1:5082';
const probe = await fetch(origin + '/healthz', {signal: AbortSignal.timeout(1000)}).catch(() => null);
if (probe) throw new Error('Port 5082 is already occupied; original host will not be stopped');
async function stopApi(api) {
  if (api.exitCode !== null) return;
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('Created acceptance API did not stop')), 5000);
    api.once('exit', () => {clearTimeout(timer); resolve();});
    api.kill();
  });
}
async function launchApi(extra) {
  const api = start([dll('api'), '--webroot', path.resolve(root, 'app-temp/web/dist')], {
    cwd: path.join(root, 'app-temp/api'), env: {...env, ASPNETCORE_ENVIRONMENT: 'Development', PORT: '5082',
      PLAYBACK_VALIDATION_PORT: '5081', PLAYBACK_VALIDATION_SESSION: 'no-unrelated-background-work', ...extra}, stdio: 'inherit',
  });
  try {
    for (let attempt = 0; attempt < 30 && api.exitCode === null; attempt++) {
      const response = await fetch(origin + '/healthz', {signal: AbortSignal.timeout(2000)}).catch(() => null);
      if (response?.ok) return api;
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
    throw new Error('Isolated acceptance API did not become ready; no container was started');
  } catch (error) {await stopApi(api); throw error;}
}
async function check(args, extra = {}) {
  const child = start([dll('checks'), ...args], {cwd: root, env: {...env, ...extra}, stdio: 'inherit'});
  const code = await new Promise((resolve, reject) => {child.once('error', reject); child.once('exit', resolve);});
  if (code !== 0) throw new Error(`Acceptance check failed (${code}); inspect retained test artifacts`);
}
await check(['--refactor-mongo']);
await check(['--notes-store-check']);
await check(['--session-sync-store-check']);
const browserEnv = {PLAYBACK_DEMO_PASSWORD: randomUUID(), PLAYBACK_PUBLIC_ORIGIN: origin};
let browserApi = await launchApi(browserEnv);
try {
  const {refactorMongoBrowser} = await import('../../web/test/refactor-mongo.check.mjs');
  await refactorMongoBrowser({origin, password: browserEnv.PLAYBACK_DEMO_PASSWORD, artifacts: run, restart: async () => {
    await stopApi(browserApi); browserApi = await launchApi(browserEnv);
  }});
} finally {await stopApi(browserApi);}
if (live) {
  if (!env.GOOGLE_AI_STUDIO_API_KEY) throw new Error('Gemini credential is unavailable in the process environment');
  const liveEnv = {...env, ASPNETCORE_ENVIRONMENT: 'Development', PORT: '5082', PLAYBACK_VALIDATION_PORT: '5081',
    PLAYBACK_VALIDATION_SESSION: 'no-unrelated-background-work', PLAYBACK_OFFLINE_TEST: 'no', PLAYBACK_PAUSE_EXTERNAL_ASR: 'no',
    PLAYBACK_SAMPLE_AUDIO_LIVE: 'yes', PLAYBACK_SAMPLE_LIVE_ORIGIN: origin, PLAYBACK_RETAIN_LIVE_TEST: 'yes', PLAYBACK_LIVE_ARTIFACT_FOLDER: run};
  // This isolated sample host is local development. Parent security configuration stays intact.
  delete liveEnv.PLAYBACK_DEMO_PASSWORD;
  delete liveEnv.PLAYBACK_PUBLIC_ORIGIN;
  const api = await launchApi(liveEnv);
  try {
    let ready = false;
    for (let attempt = 0; attempt < 30 && api.exitCode === null; attempt++) {
      const response = await fetch(origin + '/api/health', {signal: AbortSignal.timeout(2000)}).catch(() => null);
      if (response?.ok) {const health = await response.json(); if (health.mongo && health.database === 'playback_e2e') {ready = true; break;}}
      await new Promise(resolve => setTimeout(resolve, 1000));
    }
    if (!ready) throw new Error('Isolated live API did not become ready; no container was started');
    await check(['--sample-audio-live', path.join(root, 'app-temp/data/test-audio/sampleAudio')], liveEnv);
  } finally {await stopApi(api);}
}
await writeFile(path.join(run, 'acceptance.json'), JSON.stringify({database: 'playback_e2e', localMongoPassed: true, liveSampleRun: live,
  browserApiRestartPassed: true, semanticQualityAccepted: false, syntheticDataRetained: true, originalHostRestarted: false}, null, 2));
console.log(`Acceptance artifacts retained in ${run}; natural note quality requires source review.`);
