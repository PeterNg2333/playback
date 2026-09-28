// Offline only: publish a local report from saved results, never contact a provider.
import { readFile, writeFile, readdir } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import path from 'node:path';
const root=path.resolve('app-temp/data/validation'),out=path.join(root,'runs/2026-09-28-repair');
const read=async name=>JSON.parse((await readFile(path.join(out,name),'utf8')).replace(/^\uFEFF/,''));
const summaries=await Promise.all(['asr-summary.json','asr-hint-auto-summary.json','asr-sample-summary.json'].map(read));
const results=await Promise.all(summaries.flatMap(x=>x.results).map(x=>read(x.file)));
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
const paths=[...new Set([
 ...execFileSync('git',['diff','--name-only'],{encoding:'utf8'}).trim().split(/\r?\n/),
 ...execFileSync('git',['ls-files','--others','--exclude-standard'],{encoding:'utf8'}).trim().split(/\r?\n/)
])].filter(p=>p.startsWith('app-temp/')&&!p.startsWith('app-temp/data/')&&/\.(cs|tsx?|css|mjs|md|json|ps1)$/.test(p));
const files=Object.fromEntries(await Promise.all(paths.map(async p=>[p,hash(await readFile(p))])));
const manifest={reportGeneratedAt:new Date().toISOString(),providerTestDate:'2026-09-28',mode:'cached provider evidence + separately dated offline checks',
 gitHead:execFileSync('git',['rev-parse','HEAD'],{encoding:'utf8'}).trim(),uncommitted:true,sourceSha256:files,
 sourceFingerprint:hash(JSON.stringify(files)),benchmarkRequests:results.length,
 providerReportedBenchmarkCostUsd:results.reduce((s,r)=>s+(r.response?.usage?.cost??0),0),
 referenceLimit:'Synthetic prescribed scripts; no independently human-verified natural Cantonese ground truth. Repo first 7 seconds not aligned; excluded from scoring.',
 runtimeMode:'OpenRouter REST + VAD and bounded preview; no native streaming provider credentials',
 artifacts:(await readdir(out)).sort()};
await writeFile(path.join(out,'manifest.json'),JSON.stringify(manifest,null,2));
const ms=r=>r.latencyMs+' ms';
const escape=v=>String(v??'—').replaceAll('|','\\|').replaceAll('\n',' ');
let md=`# 2026-09-28 修復驗收與模型比較\n\n離線報告生成：${manifest.reportGeneratedAt}。Provider 結果原始日期為 2026-09-28；重建報告不呼叫 API。HEAD ${manifest.gitHead} 加未提交修改；完整檔案 hash 見 [manifest](manifest.json)。新 UI 回歸與較早 provider 結果分開記錄。\n\n`;
md+=`## 結論及未完成條件\n\n- 暫保留 Qwen 1.7B 為 mixed Cantonese 預設：本批合成短音較能保留佢哋／我哋／喺／而家，但仍將 cache 聽成 cash、緩存聽錯、唔係寫成唔系。不是已達自然課堂準確度驗收。Whisper/Turbo 在部分粵語案例改寫為普通話；三款 English 合成短音均接近指定文稿。\n- OpenRouter 三款 STT endpoint 真實請求共 ${results.length} 次，回報費用合計 USD ${manifest.providerReportedBenchmarkCostUsd.toFixed(8)}。這只包含比較 runner；另有 8 次 application final、至少 6 次成功 interim、12 次 Jev rank 及 9 次 Gemini 工作，見 [call accounting](call-accounting.json)。較早取消／timeout 的 preview 次數未完整記錄；沒有完整帳單資訊，不能把比較費用當總費用。\n- 真實 system loopback 已通過灰字/final、pause/resume/reload、stop/restart；首次可見灰字 3.460 秒，見 [畫面](hardware-interim-visible.png) 與 [原始觀察](visible-interim.json)。較早一次所有 preview 碰到 6 秒 timeout，見 [失敗保留](hardware-capture.json)。改成 15 秒有界 single-flight 後 [重測](hardware-preview-retest.json) 通過；不能保證每次 3 秒。\n- **仍有慢請求**：第一輪硬件 final 處理達 46.211 / 41.573 秒；重測 0.896 / 1.586 / 3.013 秒，最後短錄 0.771 秒。這是 server 的 VAD + provider 處理耗時，不含錄音累積／queue 等待，也不是完整最後 sample 到 UI 的精確 latency。數據來自 server.log；加長 timeout 不代表 provider 變快。\n- 原生 streaming 未完成：OpenRouter STT 文件沒有 PCM/WebSocket input + partial 協議；目前沒有可用的直接 provider key。具體 adapter 仍為 REST；UI 明示 fallback。\n- 沒有獨立人工核對的自然粵語 corpus，沒有連續三小時實機壓力測試。固定 8 秒最長边界仍可能切斷英文詞；沒有聲稱 overlap 品質已解決。\n\n`;
md+='## 驗收證據\n\n| 項目 | 結果與證據 |\n|---|---|\n';
for(const row of [
 ['ASR model/hint/raw/display','21 次直接 STT 比較，三款 200；另從 UI 選 Turbo/en、重載、上傳11秒英語，正式 transcript 記錄正確模型/hint。[真實首次](app-model-selection-first-attempt.json) 2212ms；截圖 selector 修復後相同 chunk 回 HTTP200 [去重 readback](app-model-selection.json)，50ms 是讀既有結果，不是新 ASR latency。已恢復 Qwen/yue-en。'],
 ['自動 Jev → web 補充 → notes','實機來源事件自動處理 cash、FFT、spectrogram；FFT confidence=0.50 過閾值，Google Search 三來源，加入 v3；筆記 CAS 拒絕過期 v2，後續重試保存 v4。[activity](hardware-activity.json) / [畫面](hardware-note-stream.png)。'],
 ['Jev context','[cache technical](jev-cache-tech.json) probability .83 / confidence .80；[toy cache](jev-cache-everyday.json) .43 / .81；[中文緩存](jev-chinese.json) .80 / .77。[cache-hit](jev-cache-hit.json) 零追加 rank。成本未回報，只列 usage tokens。'],
 ['Ask Playback 真實 lecture','[有引用回答](chat-lecture-reused.json)，web 關，material ID 有效；同一 request ID readback 沒有重複模型工作。'],
 ['Ask Playback 真實 regression','[ashion / What is cache + web](chat-mismatch-reused.json)：lecture 不足、修正只是假設，web 獨立回答及 citation。[原始 live 畫面](live-mismatch-web.png)。早期 browser-mismatch.json 失敗是 iframe localStorage 測試 harness，實際回答成功；已修復 harness 並 [離線重播驗收](offline-replay.json)。'],
 ['Timer / sources','[實機計時](hardware-preview-retest.json) 與 [E2E](e2e-checks.log)；一小時顯示為離線 fixture，並非錄足一小時。'],
 ['History / keyboard / touch / narrow','[實際 history](live-history-final.png)、[實際窄屏 history](live-history-final-narrow.png)、[320px touch 離線驗收](replay-history-touch-320.png)、[320px chatbot](replay-chat-320.png)，hover/focus/click/Escape、點擊命中及 viewport bounds 通過。'],
 ['Dirty note / late answers / errors','[離線結果](offline-replay.json)：未保存文字與 selection/caret、重複送出、timeout、截斷串流、換 session 後 late answer。圖片為已保存真實回應的 offline replay，不冒充新 live。'],
  ['Build / protocol / API','[API build](api-build.log)、[protocol](protocol-checks.log)、[integration](integration-checks.log)、[UI](ui-checks.log)、[E2E](e2e-checks.log)；前端 build 的既有 bundle size 警告另列於 build log。'],
  ['最後版本筆記／日常服務','[最後 Gemini revision](browser-notes-final.json) 保存 v5，cache/FFT 補充各一段 [畫面](final-live-notes-saved.png)。[新版 5078 health](primary-after-health.json) 與 [日常 5173 browser check](primary-browser-check.log) 已讀回核對。舊 paused recording 已正常 Stop，97 段與 notes 保留；已核對所有 session 的 pending ASR 為零；正常自動重試/recovery 和新錄音自動化已啟用。'],
 ])md+=`| ${row[0]} | ${row[1]} |\n`;
md+='\n## 同音訊比較\n\nSynthetic reference 是合成器指定的文稿，不冒充人工聽寫。字形轉換只改 script；語义錯、漏詞、普通話改寫分開評述，不用一個 CER 混合計分。Repo 0–7 秒三款只回傳 Um／Thank you 等，與原先取用的全文第一句不相符：**未建立對齊，排除準確度評分**，原 metadata 的對齊字眼已在 [correction](reference-correction.json) 更正。\n\n| case / hint | model | latency | provider text | app display | cost USD |\n|---|---|---:|---|---|---:|\n';
for(const r of results)md+=`| ${escape(r.config.case)} / ${r.config.language??'auto'} | ${r.config.model} | ${ms(r)} | ${escape(r.response?.text)} | ${escape(r.appDisplay??r.response?.text)} | ${r.response?.usage?.cost??'not reported'} |\n`;
md+='\n### 指定 reference 與對照重點\n\n';
for(const id of ['cantonese','mixed','english']){
 const r=results.find(x=>x.config.case===id);md+=`- **${id} reference（合成文稿）**：${r.reference}\n`;
}
md+='\n- 粵語：Qwen/V3 保留佢哋、我哋、喺、而家，Turbo 的他们／我们／不是屬語體改寫；Qwen 換全／V3 完全／Turbo 缓传 均未正確辨識緩存。繁體轉換不能修復。\n- Mixed：Qwen 保留我哋、唔系、呢度及 FFT/spectrogram；cache → cash 是語義誤聽。Whisper/Turbo 部分句轉為我們／不是／這裏。\n- yue vs auto：同一粵語音訊分開三款各測一次，見表；HTTP 200 證明欄位可送，不證明模型採用了 hint 或一定出繁體。\n- 靜音／固定低幅背景聲：三款均回空文字；本機 VAD 更在上傳前跳過，見 [VAD 範圍](vad-fixtures.json)。這不能代表所有環境噪音。\n- REST direct STT 沒有 interim，表中 latency 是整段 HTTP 往返；硬件開始到灰字另測；final server 處理耗時另列，沒有精確最後 sample 到 UI timestamp。逐字稿時間為本機 capture range，不宣稱 provider word timestamps。\n\n';
md+='## Provider 官方協議\n\n- [OpenRouter STT](https://openrouter.ai/docs/guides/overview/multimodal/stt)：固定 /audio/transcriptions JSON input_audio；不是 chat audio-understanding endpoint。目錄及 endpoint 回應保存在本 run。\n- [Qwen realtime](https://www.alibabacloud.com/help/en/model-studio/qwen-asr-realtime-api)：直接 provider 的 WebSocket audio frames / VAD 能力，尚無對應有效 key，沒有冒用 OpenRouter key。\n- [TypeSafe Jev schema](https://api.typesafe.ai/openapi.json)：model discovery、noul/choice/confidence。\n- [Agent Framework streaming](https://learn.microsoft.com/en-us/agent-framework/concepts/agents/running-agents) 與 [Google Search grounding](https://docs.cloud.google.com/gemini-enterprise-agent-platform/models/grounding/grounding-with-google-search)。\n\n';
md+='## 保存與重用\n\n`node app-temp/checks/validation-report.mjs` 只讀本地結果。`validation-asr.mjs` 無 --live 時不請求 provider；fingerprint 包含音訊 hash、model/provider/endpoint、hint、VAD/normalization、case、adapter code hash。--refresh 才重送同一案例（包含失敗）。UI replay 封鎖外部 https，證據保持在本資料夾，不自動清理，不加入 Git。\n';
await writeFile(path.join(out,'report.zh-HK.md'),md);
await writeFile(path.join(root,'index.md'),`# Playback 本機驗收索引\n\n2026-09-28 provider 測試；${manifest.reportGeneratedAt} 離線整理。不是每次 build 重新驗證 provider；之後的可用性可能變動。\n\n**暫選 Qwen 1.7B（REST fallback）**。合成混合粵英較保留口語，仍有 cache/cash 與中文術語誤聽。\n\n- [完整結果、最好／最差及限制](runs/2026-09-28-repair/report.zh-HK.md)\n- [版本／hash／費用 manifest](runs/2026-09-28-repair/manifest.json)\n- [真實可見灰字](runs/2026-09-28-repair/hardware-interim-visible.png)\n- [修復後 chatbot 的 320px 畫面（cached replay）](runs/2026-09-28-repair/replay-chat-320.png)\n- [ashion regression 真實回應](runs/2026-09-28-repair/chat-mismatch-reused.json)\n- [離線 UI 驗收](runs/2026-09-28-repair/offline-replay.json)\n\n已通過：provider 比較、實機 REST 灰字/final、計時、Jev contextual decision、自動 web 解釋與 notes、source-backed chatbot、history 及窄屏。\n\n仍有限制：自然粵語準確度未通過獨立人工 corpus 驗收；repo sample 0–7 秒未對齊，排除評分；native streaming 無直接 provider key；未做三小時實機壓力測試；REST 最大边界仍可能截詞。第一次 preview timeout 保存並有修復重測。詳情以報告及個別有日期的結果為準。\n`);
console.log(`Offline report saved: ${results.length} STT responses, ${paths.length} source hashes; no network.`);
