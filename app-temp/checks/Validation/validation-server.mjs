import { spawn } from 'node:child_process';
import { mkdir, appendFile, writeFile, readFile } from 'node:fs/promises';
import path from 'node:path';
if(!process.argv.includes('--live')) throw Error('Explicit --live is required; start/build/tests do not start paid validation.');
const output=path.resolve('app-temp/data/validation/runs/2026-09-28-repair');
await mkdir(output,{recursive:true});
const env={...process.env,ASPNETCORE_ENVIRONMENT:'Development',PLAYBACK_VALIDATION_PORT:'5081',PLAYBACK_MONGO_DATABASE:'playback_e2e',PLAYBACK_AUTO_NOTES:'no',PLAYBACK_AUTO_TERMS:'no',PLAYBACK_OFFLINE_TEST:'no',PLAYBACK_PAUSE_EXTERNAL_ASR:'no'};
if(process.argv.includes('--automatic')) {
  const fixture=JSON.parse(await readFile(path.join(output,'app-session.json'),'utf8'));
  env.PLAYBACK_VALIDATION_SESSION=fixture.id;
  env.PLAYBACK_AUTO_NOTES='yes';env.PLAYBACK_AUTO_TERMS='yes';
}
try { const r=await fetch('http://127.0.0.1:5081/api/health',{signal:AbortSignal.timeout(1500)}); if(r.ok)throw Error('Port 5081 is already in use'); }
catch(error){if(error.message==='Port 5081 is already in use')throw error;}
const binary=process.argv.includes('--current')?'current':'verification';
const child=spawn('dotnet',[`app-temp/api/bin/${binary}/net10.0/Playback.Api.dll`],{env,windowsHide:true,stdio:['ignore','pipe','pipe']});
await writeFile(path.join(output,'server-process.json'),JSON.stringify({pid:child.pid,startedAt:new Date().toISOString(),automatic:process.argv.includes('--automatic')},null,2));
let serial=Promise.resolve();
for(const stream of [child.stdout,child.stderr]) stream.on('data',bytes=>{
  let value=bytes.toString();for(const name of ['OPENROUTER_API_KEY','GOOGLE_AI_STUDIO_API_KEY','JEV_API_KEY'])if(env[name])value=value.split(env[name]).join('[redacted]');
  serial=serial.then(()=>appendFile(path.join(output,'server.log'),value));
});
child.on('exit',code=>{console.log(`Validation API exited ${code}`);process.exitCode=code??1;});
console.log(`Validation API starting on 5081, PID ${child.pid}; existing recording untouched.`);
process.on('SIGINT',()=>child.kill());process.on('SIGTERM',()=>child.kill());
