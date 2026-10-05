// Explicit paid requests only with --live. Same fingerprint, including failures, is reused unless --refresh.
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
const root = path.resolve('app-temp/data/validation');
const output = path.join(root, 'runs/2026-09-28-repair');
await mkdir(output, {recursive:true});
const live = process.argv.includes('--live'), refresh = process.argv.includes('--refresh');
const hash = value => createHash('sha256').update(value).digest('hex');
const code = hash(await readFile('app-temp/api/Audio/Asr/Providers/OpenRouterAsrClient.cs'));
const endpoint = 'https://openrouter.ai/api/v1/audio/transcriptions';
const models = ['qwen/qwen3-asr-1.7b','openai/whisper-large-v3','openai/whisper-large-v3-turbo'];
const read = async file => { try { return JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,'')); } catch { return null; } };
const safe = value => {
  let text = JSON.stringify(value);
  for (const name of ['OPENROUTER_API_KEY','GOOGLE_AI_STUDIO_API_KEY','JEV_API_KEY'])
    if (process.env[name]) text = text.split(process.env[name]).join('[redacted]');
  return JSON.parse(text);
};
async function bounded(response) {
  const reader = response.body.getReader(); const parts=[]; let bytes=0;
  while(true) { const {value,done}=await reader.read(); if(done)break;
    bytes+=value.length; if(bytes>2_000_000){await reader.cancel();throw Error('Response size limit');} parts.push(Buffer.from(value)); }
  return JSON.parse(Buffer.concat(parts).toString('utf8'));
}
let catalogue = await read(path.join(output,'model-catalogue.json'));
if (live && (!catalogue || refresh)) {
  const response = await fetch('https://openrouter.ai/api/v1/models?output_modalities=transcription', {redirect:'error',signal:AbortSignal.timeout(20000)});
  catalogue = {testedAt:new Date().toISOString(),httpStatus:response.status,body:await bounded(response)};
  await writeFile(path.join(output,'model-catalogue.json'),JSON.stringify(catalogue,null,2));
  for(const model of models) {
    const resp = await fetch(`https://openrouter.ai/api/v1/models/${model}/endpoints`,{redirect:'error',signal:AbortSignal.timeout(20000)});
    await writeFile(path.join(output,model.split('/')[1]+'-endpoints.json'),JSON.stringify({httpStatus:resp.status,body:await bounded(resp)},null,2));
  }
}
const fixtures = await read(path.join(root,'fixtures/synthetic.json'));
if(!fixtures) throw Error('Prepare fixtures first: powershell -File app-temp/checks/Validation/prepare-validation.ps1');
// Silence and deterministic noise are independent no-speech controls.
function wave(noise) {
  const data=Buffer.alloc(44+16000*2*3); data.write('RIFF'); data.writeUInt32LE(data.length-8,4);data.write('WAVEfmt ',8);data.writeUInt32LE(16,16);
  data.writeUInt16LE(1,20);data.writeUInt16LE(1,22);data.writeUInt32LE(16000,24);data.writeUInt32LE(32000,28);data.writeUInt16LE(2,32);data.writeUInt16LE(16,34);data.write('data',36);data.writeUInt32LE(data.length-44,40);
  let seed=17; if(noise)for(let i=44;i<data.length;i+=2){seed=(seed*1664525+1013904223)>>>0;data.writeInt16LE(Math.round(((seed/4294967296)*2-1)*200),i);}return data;
}
for(const id of ['silence','background']) {const audio=wave(id==='background');await writeFile(path.join(root,'fixtures',id+'.wav'),audio);fixtures.push({id,reference:'',referenceKind:'deterministic no-speech control',audioSha256:hash(audio)});}
const cases=fixtures.flatMap(fixture=>models.map(model=>({fixture,model,language:fixture.id==='cantonese'?'yue':fixture.id==='english'?'en':null})));
if(process.argv.includes('--sample')) {
  const fixture=await read(path.join(root,'fixtures/sample-0-7.json'));
  if(!fixture)throw Error('Run --validation-fixtures first');
  cases.splice(0,cases.length,...models.map(model=>({fixture,model,language:'en'})));
}
if(process.argv.includes('--hint-auto')) {
  cases.splice(0,cases.length,...models.map(model=>({fixture:fixtures.find(x=>x.id==='cantonese'),model,language:null})));
}
if(process.argv.includes('--case')) {const id=process.argv[process.argv.indexOf('--case')+1];for(let i=cases.length-1;i>=0;i--)if(cases[i].fixture.id!==id)cases.splice(i,1);}
const results=[];
let sent=0;
for(const {fixture,model,language} of cases) {
  const audio=await readFile(path.join(root,'fixtures',fixture.id+'.wav'));
  const config={endpoint,model,provider:'openrouter; upstream routing determined by provider',language,audioSha256:hash(audio),normalization:'none; script and semantic assessment separate',vad:'fixture upload; untrimmed',adapterCodeSha256:code,case:fixture.id};
  const fingerprint=hash(JSON.stringify(config));const file=path.join(output,`asr-${fixture.id}-${model.split('/')[1]}-${language??'auto'}-${fingerprint.slice(0,10)}.json`);
  let result=await read(file);
  if(!result || refresh) {
    if(!live){console.log(`MISSING ${fixture.id} ${model} ${language??'auto'}`);continue;}
    if(!process.env.OPENROUTER_API_KEY)throw Error('OpenRouter credential unavailable');
    const start=performance.now();sent++;
    try {
      const response=await fetch(endpoint,{method:'POST',redirect:'error',signal:AbortSignal.timeout(75000),headers:{'Content-Type':'application/json',Authorization:`Bearer ${process.env.OPENROUTER_API_KEY}`},
        body:JSON.stringify({model,input_audio:{data:audio.toString('base64'),format:'wav'},response_format:'json',...(language?{language}:{})})});
      result={config,fingerprint,testedAt:new Date().toISOString(),latencyMs:Math.round(performance.now()-start),httpStatus:response.status,firstInterimMs:null,reference:fixture.reference,referenceKind:fixture.referenceKind,response:safe(await bounded(response))};
    } catch(error){result={config,fingerprint,testedAt:new Date().toISOString(),latencyMs:Math.round(performance.now()-start),error:error.name,reference:fixture.reference};}
    await writeFile(file,JSON.stringify(result,null,2));
  }
  results.push({...result,file:path.basename(file)});
  console.log(`${fixture.id} ${model} language=${language??'auto'} http=${result.httpStatus??result.error} ${result.latencyMs}ms ${live?'saved/reused':'cached'}`);
}
await writeFile(path.join(output,process.argv.includes('--sample')?'asr-sample-summary.json':process.argv.includes('--hint-auto')?'asr-hint-auto-summary.json':'asr-summary.json'),JSON.stringify({generatedAt:new Date().toISOString(),newRequests:sent,code,results},null,2));
console.log(`New paid ASR requests: ${sent}. Results: ${output}`);
