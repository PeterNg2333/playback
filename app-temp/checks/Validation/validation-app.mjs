import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import path from 'node:path';
const base='http://127.0.0.1:5081/api';
const root=path.resolve('app-temp/data/validation');
const out=path.join(root,'runs/2026-09-28-repair');
await mkdir(out,{recursive:true});
const live=process.argv.includes('--live'), refresh=process.argv.includes('--refresh');
const phase=process.argv[process.argv.indexOf('--phase')+1]??'report';
const read=async file=>{try{return JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,''));}catch{return null;}};
if(!live){console.log('Offline: read saved results in '+out+'. Use --live --phase jev|seed|chat for explicit new calls.');process.exit(0);}
const health=await (await fetch(base+'/health')).json();
if(!health.mongo||health.database!=='playback_e2e'||!health.groundedChatFallback)throw Error('Unexpected API version or database');
await writeFile(path.join(out,'app-health.json'),JSON.stringify(health,null,2));
async function call(name,endpoint,body,method='POST') {
  const file=path.join(out,name+'.json');
  const previous=await read(file);
  if(previous&&!refresh){console.log(name+': cached '+previous.testedAt);return previous.response;}
  const start=performance.now();let result;
  try{
    const resp=await fetch(base+endpoint,{method,headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(145000)});
    const response=await resp.json();
    result={testedAt:new Date().toISOString(),build:health.build,endpoint,request:body,latencyMs:Math.round(performance.now()-start),httpStatus:resp.status,response};
  }catch(error){result={testedAt:new Date().toISOString(),build:health.build,endpoint,request:body,latencyMs:Math.round(performance.now()-start),error:error.name};}
  await writeFile(file,JSON.stringify(result,null,2));
  console.log(`${name}: ${result.httpStatus??result.error} ${result.latencyMs}ms`);
  if(result.httpStatus!==200&&result.httpStatus!==201)throw Error(`${name} failed; inspect saved redacted result`);
  return result.response;
}
if(phase==='jev') {
  const cases=[
    ['cache-tech','cache','我哋而家講 cache。A cache stores frequently used data and reduces database latency.'],
    ['cache-everyday','cache','The children found a hidden cache of old toys under the stairs.'],
    ['fft','FFT','An FFT efficiently computes the discrete Fourier transform in digital signal processing.'],
    ['spectrogram','spectrogram','A spectrogram displays frequency energy over time using short-time Fourier transforms.'],
    ['chinese','緩存','我哋而家講緩存，將常用數據暫時放喺記憶體，減少讀取資料庫嘅延遲。'],
    ['everyday','lunch','We will have lunch at noon and then go home.'],
  ];
  for(const [name,term,context] of cases)await call('jev-'+name,'/terms/rank',{term,context});
  const [,term,context]=cases[0];await call('jev-cache-hit','/terms/rank',{term,context});
}
if(phase==='seed') {
  let session=await read(path.join(out,'app-session.json'));
  if(!session){session=await call('create-app-session','/sessions',{title:'Validation 2026-09-28 – ASR to notes and chat'});await writeFile(path.join(out,'app-session.json'),JSON.stringify(session,null,2));}
  await call('app-settings',`/sessions/${session.id}/languages`,{asrLanguage:'yue-en',noteLanguage:'zh-Hant',asrModel:'qwen/qwen3-asr-1.7b'},'PUT');
  await call('app-material',`/sessions/${session.id}/materials`,{name:'Synthetic cache context',text:'**cache**: A cache stores frequently used data to reduce latency. It trades additional memory for fewer database reads. 課堂測試用合成教材。'});
  const audio=await readFile(path.join(root,'fixtures/mixed.wav'));const sha=createHash('sha256').update(audio).digest('hex');
  const file=path.join(out,'app-asr-upload.json');
  if(!await read(file)){
    const form=new FormData();for(const [key,value] of Object.entries({sessionId:session.id,sourceId:'validation',sequence:1,startMs:0,endMs:12319,sha256:sha}))form.set(key,String(value));
    form.set('file',new Blob([audio],{type:'audio/wav'}),'mixed.wav');
    const start=performance.now();const response=await(await fetch(base+'/chunks',{method:'POST',body:form})).json();
    await writeFile(file,JSON.stringify({testedAt:new Date().toISOString(),build:health.build,request:{sessionId:session.id,audioSha256:sha},latencyMs:performance.now()-start,response},null,2));
  }
  for(let n=0;n<80;n++){
    const view=await(await fetch(base+`/sessions/${session.id}`)).json();
    if(view.transcripts.length || view.chunks.some(x=>x.status==='asr-manual')){await writeFile(path.join(out,'app-after-asr.json'),JSON.stringify(view,null,2));console.log('App ASR observed: '+view.chunks.map(x=>x.status).join(','));break;}
    await new Promise(r=>setTimeout(r,500));
  }
}
if(phase==='chat') {
  const session=await read(path.join(out,'app-session.json'));
  await call('chat-lecture',`/sessions/${session.id}/ask`,{question:'How does a cache reduce database reads in these notes?',useWeb:false,requestId:randomUUID()});
  await call('chat-no-evidence',`/sessions/${session.id}/ask`,{question:'What was the exact price of a ticket to Saturn?',useWeb:false,requestId:randomUUID()});
}
if(phase==='seed-mismatch') {
  const session=await call('create-mismatch-session','/sessions',{title:'E2E demo Validation 2026-09-28 – ashion regression'});
  await writeFile(path.join(out,'mismatch-session.json'),JSON.stringify(session,null,2));
  const audio=await readFile(path.join(root,'fixtures/silence.wav'));const sha=createHash('sha256').update(audio).digest('hex');
  const form=new FormData();for(const [key,value] of Object.entries({sessionId:session.id,sourceId:'fixture',sequence:1,startMs:0,endMs:3000,sha256:sha}))form.set(key,String(value));
  form.set('file',new Blob([audio],{type:'audio/wav'}),'silence.wav');
  const chunk=await(await fetch(base+'/chunks',{method:'POST',body:form})).json();
  await call('mismatch-source',`/testing/sessions/${session.id}/transcripts`,{chunkId:chunk.id,text:'Synthetic ashion'});
}
