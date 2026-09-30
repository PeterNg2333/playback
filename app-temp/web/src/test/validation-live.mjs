import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright-core';
import path from 'node:path';
const output=path.resolve('app-temp/data/validation/runs/2026-09-28-repair');
const phase=process.argv[process.argv.indexOf('--phase')+1]??'chat';
if(!process.argv.includes('--live')) { console.log('Offline: saved browser evidence at '+output+'. Add --live to make the selected bounded calls.');process.exit(0); }
const doneFile=path.join(output,'browser-'+phase+'.json');
try {await readFile(doneFile);if(!process.argv.includes('--refresh')){console.log('Reused saved browser result (including failure); --refresh required to retry: '+doneFile);process.exit(0);}}catch{}
await mkdir(output,{recursive:true});
const session=JSON.parse(await readFile(path.join(output,phase==='mismatch'?'mismatch-session.json':'app-session.json')));
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
const page=await browser.newPage({viewport:{width:1440,height:1000}});
const errors=[];page.on('pageerror',e=>errors.push(e.message));
const requests=[];const replies=[];
const pendingReplies=[];
page.on('request',r=>{if(r.method()==='POST'&&/\/api\//.test(r.url()))requests.push({url:r.url(),body:r.postData(),at:Date.now()});});
page.on('response',r=>{if(r.request().method()==='POST'&&/\/api\//.test(r.url()))pendingReplies.push((async()=>{try{replies.push({url:r.url(),status:r.status(),body:await r.text()});}catch{}})());});
await page.addInitScript(id=>{if(window===window.top)localStorage.setItem('playback-session',id);},session.id);
const screenshot=async name=>page.screenshot({path:path.join(output,(phase==='notes-final'?'final-':'')+name+'.png'),fullPage:false});
let passed=false;
try {
  await page.goto('http://127.0.0.1:5181/');await page.getByRole('heading',{name:session.title,exact:true}).waitFor();
  await page.getByText('Synthetic cache context',{exact:true}).count();
  if(phase==='notes'||phase==='notes-final') {
    const response=page.waitForResponse(r=>r.url().endsWith('/notes/generate')&&r.request().method()==='POST',{timeout:145000});
    await page.getByRole('button',{name:'Revise with AI',exact:true}).click();
    await page.locator('.note-ai-status').waitFor({timeout:25000});
    await screenshot('live-notes-running');
    await page.getByRole('button',{name:/^Live draft/}).click();
    await page.locator('.note-draft .markdown-preview').waitFor({timeout:80000});await screenshot('live-notes-stream');
    assert.equal((await response).status(),200);
    await page.locator('.note-ai-status').waitFor({state:'detached',timeout:10000});
    await page.getByRole('button',{name:'Preview',exact:true}).click();
    await page.waitForTimeout(4500);
    await screenshot('live-notes-saved');
  }
  if(phase==='terms') {
    // Source events/background recovery trigger Jev; opening a saved explanation must remain read-only.
    const term=page.locator('.term-highlight').first();await term.waitFor({timeout:145000});await term.hover();
    await page.locator('.term-explanation .markdown-preview').waitFor({timeout:145000});
    await screenshot('live-term-hover');await page.keyboard.press('Escape');
    await page.getByRole('button',{name:'AI activity history',exact:true}).hover();await screenshot('live-history');
    const box=await page.locator('#notes-activity').boundingBox();assert(box&&box.x>=0&&box.y>=0&&box.x+box.width<=1440);
    await page.locator('#notes-activity').hover();assert(await page.locator('#notes-activity').isVisible());
    await page.keyboard.press('Escape');assert.equal(await page.locator('#notes-activity').count(),0);
  }
  if(phase==='mismatch') {
    const row=page.locator('.transcript-row').filter({hasText:'ashion'}).first();await row.waitFor();
    await row.locator('summary span').filter({hasText:/Synthetic ashion/}).evaluate(el=>{
      const range=document.createRange();const node=el.firstChild;const at=node.textContent.indexOf('ashion');range.setStart(node,at);range.setEnd(node,at+6);window.getSelection().removeAllRanges();window.getSelection().addRange(range);
    });
    await row.locator('summary.original').dispatchEvent('mouseup');
    await page.getByRole('button',{name:/Ask Playback about selection/}).click();
    await page.getByLabel('Your question',{exact:true}).fill('What is cache');
    await page.locator('.web-toggle input').check();
    const response=page.waitForResponse(r=>r.url().endsWith('/ask/stream'),{timeout:145000});
    await page.getByRole('button',{name:'Send',exact:true}).click();await screenshot('live-mismatch-loading');
    assert.equal((await response).status(),200);await page.locator('.answer').waitFor({timeout:140000});
    await page.getByText('Public web:',{exact:false}).waitFor();
    const text=await page.locator('.answer').innerText();assert.match(text,/not enough verified lecture evidence/);assert.match(text,/hypothesis/);assert.match(text,/cache/i);
    await screenshot('live-mismatch-web');
    await page.setViewportSize({width:375,height:812});await page.waitForTimeout(350);await screenshot('live-mismatch-narrow');
  }
  if(phase==='chat') {
    await page.getByRole('button',{name:'◇ Ask Playback',exact:true}).click();
    await page.getByLabel('Your question',{exact:true}).fill('How does a cache reduce database reads in these notes?');
    const response=page.waitForResponse(r=>r.url().endsWith('/ask/stream'),{timeout:145000});
    await page.getByRole('button',{name:'Send',exact:true}).click();
    await page.locator('.chat-form').dispatchEvent('submit'); // same-render duplicate must be refused
    await page.getByRole('button',{name:'Working…',exact:true}).waitFor();await screenshot('live-chat-loading');
    await page.locator('.provisional-answer').waitFor({timeout:100000});await screenshot('live-chat-stream');
    assert.equal((await response).status(),200);
    await page.locator('.answer').waitFor({timeout:20000});assert(await page.locator('.answer .citation').count()>0);
    await screenshot('live-chat-lecture');
    assert.equal(requests.filter(x=>x.url.endsWith('/ask/stream')).length,1);
    await page.setViewportSize({width:375,height:812});await page.waitForTimeout(350);await screenshot('live-chat-narrow');
    const box=await page.locator('.chat').boundingBox();assert(box&&box.x>=0&&box.x+box.width<=375&&box.y>=0);
  }
  assert.deepEqual(errors,[]);passed=true;
}finally{
  const visibleAnswer=await page.locator('.answer').innerText().catch(()=>null);
  // Consume the exact same request ID at the JSON endpoint: server reuses the completed task.
  for(const req of requests.filter(x=>x.url.endsWith('/ask/stream'))){
    const response=await fetch(req.url.replace('http://127.0.0.1:5181','http://127.0.0.1:5081').replace('/ask/stream','/ask'),{method:'POST',headers:{'Content-Type':'application/json'},body:req.body});
    replies.push({url:req.url,status:response.status,sameRequestReadback:true,body:await response.text()});
  }
  await Promise.allSettled(pendingReplies);
  await writeFile(doneFile,JSON.stringify({testedAt:new Date().toISOString(),phase,passed,visibleAnswer,requests,replies,errors},null,2));
  await browser.close();
}
console.log('Live browser '+phase+' passed; results saved.');
