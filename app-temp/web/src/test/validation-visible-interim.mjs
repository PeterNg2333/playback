import { chromium } from 'playwright-core';
import { readFile, writeFile } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import path from 'node:path';
import assert from 'node:assert/strict';
const out=path.resolve('app-temp/data/validation/runs/2026-09-28-repair');
const file=path.join(out,'visible-interim.json');
if(!process.argv.includes('--live')){console.log('Offline: '+file);process.exit(0);}
try{await readFile(file);if(!process.argv.includes('--refresh')){console.log('Reusing saved visible interim result.');process.exit(0);}}catch{}
const session=JSON.parse(await readFile(path.join(out,'app-session.json')));
const base='http://127.0.0.1:5081/api';
const get=async p=>(await fetch(base+p)).json();
const post=async(p,body)=>(await fetch(base+p,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body??{})})).json();
assert.equal((await get('/capture/status')).state,'idle');
const health=await get('/health');assert.equal(health.autoNotes,false);assert.equal(health.autoTerms,false);
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'});
const page=await browser.newPage({viewport:{width:1440,height:1000}});let player,passed=false,elapsedMs,observed;
try{
 await page.addInitScript(id=>localStorage.setItem('playback-session',id),session.id);
 await page.goto('http://127.0.0.1:5181');await page.getByRole('heading',{name:session.title,exact:true}).waitFor();
 await post('/capture/start',{sessionId:session.id,sourceMode:'system'});const start=Date.now();
 const audio=path.resolve('app-temp/data/validation/fixtures/mixed.wav').replaceAll("'","''");
 player=spawn('powershell.exe',['-NoProfile','-Command',`$player=New-Object System.Media.SoundPlayer '${audio}'; $player.PlaySync(); $player.Dispose()`],{windowsHide:true,stdio:'ignore'});
 const gray=page.locator('.interim-text').last();await gray.waitFor({timeout:19000});await gray.scrollIntoViewIfNeeded();
 elapsedMs=Date.now()-start;observed=await get('/capture/status');
 assert(await gray.isVisible());await page.screenshot({path:path.join(out,'hardware-interim-visible.png')});passed=true;
}finally{
 player?.kill();await post('/capture/stop');
 await writeFile(file,JSON.stringify({testedAt:new Date().toISOString(),build:health.build,passed,elapsedMs,observed},null,2));await browser.close();
}
console.log('Real provider gray text visibly captured in '+elapsedMs+'ms; sample recording stopped.');
