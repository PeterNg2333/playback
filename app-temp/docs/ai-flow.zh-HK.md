# 2026-09-28：錄音到筆記及 Ask Playback 的實際流程

以下是較早的3.5 live及2026-09-28流程記錄。2026-09-29新程式預設3.1 Flash-Lite、每10秒由Jev note gate決策、只更新選定sections、Reading／Sources／詳解／group flow及virtualized transcript；現行行為與未驗證項以 [逐項驗收](../../docs/notes-redesign-validation.zh-HK.md) 及 [notes-chat-update](notes-chat-update.zh-HK.md) 為準。Live runtime未更新，新prompt／gate自然內容沒有新增付費驗證。

本版使用 .NET 10、Microsoft Agent Framework、Vertex Express `gemini-3.5-flash-lite`、TypeSafe Jev，以及 OpenRouter transcription REST。結果索引在本機 [`../data/validation/index.md`](../data/validation/index.md)。這個目錄被 Git ignore；報告、raw response、音訊和截圖不提交。

## 例子：「我哋而家講 cache」

1. Windows WASAPI 按 microphone／system 分開保存原始 WAV；Stopwatch 計算本次 Record 的累計時間，pause 時停止。保存 offset 與本次 elapsed 不同，重新 Record 的 elapsed 由零起。
2. 本機 Silero VAD 判斷人聲。辨識音訊保留 onset 前 300 ms、停止後約 500–700 ms；原始檔不裁剪。純數位靜音及 VAD 無人聲片段不送外部 ASR，仍可回聽。VAD 有誤判可能，安靜音量本身不是丟棄條件。
3. 目前是 **REST fallback**。累積新語音後最早約 2 秒請求預覽，每 source 一個請求、15 秒 deadline、有限音訊緩衝；沒有新增人聲便不重送相同静音尾巴。正式工作有積壓時抑制預覽。最長約 8 秒封存，滿 3 秒且全部 source 已安靜 700 ms 時可提早封存。最大边界仍可能截詞；本版未有經品質驗收的 overlap merge。
4. 預覽回傳先以灰字顯示，保存 raw、顯示文字和範圍於記憶體。廣東話 `yue` 送 language hint；`yue-en` 不送單一語言 hint，以 auto 辨識配合繁體顯示。raw「我哋而家讲」可顯示「我哋而家講」，但 `cash` 不會憑空改成 `cache`。已有簡體逐字稿亦按 session 顯示偏好轉換，raw 不改、不重上傳。
5. 正式 WAV 經本機 VAD 裁出辨識範圍，以 session/source/sequence/hash 識別；queue 最多兩個 final worker。本機 chunk ID 和已完成 transcript 防止重複保存；不能保證外部 provider 對 Idempotency-Key 的計費去重。正式稿到達後按 source/time range 取代灰字。模型／語言在請求開始時讀 session；更改後下一個 request/retry 使用新值，過時預覽不顯示。
6. Final transcript／教材觸發 candidate extraction。大小寫縮寫、小寫 `cache`、中文「緩存」及句內引入的詞都可成為候選。候選只是召回，**是否解釋由 Jev 真實回應決定**。Jev 的 state 帶原詞及附近來源上下文；cache key 包含 term/context。自動工作預設開啟（`PLAYBACK_AUTO_TERMS=no` 可關），來源事件延遲約 4 秒、worker 每 2 秒檢查、每次最多三詞、最多 128 個待處理 session。
7. 門檻為 explain probability ≥ 0.75、category high/medium、confidence ≥ 0.50。真實測試 technical cache=.83/.80，而 toy cache=.43/.81；後者不高亮。FFT 曾因 confidence=.43 不通過，較明確 context 的另一次 .50 通過。低 confidence 不用本機大寫規則强行補成成功。
8. Jev 通過後立即呼叫 Gemini + Google Search，不等使用者點詞。沒有 HTTPS grounding source 就失敗、不假造 citations。短解釋以「AI／網絡補充」加入新 note version；詳解及來源保存在 term_insights，可長按／鍵盤開啟 ref。term/context/output language/explanation version 形成保存身份；同詞不同意思不共用解釋。失敗可在三次有界自動嘗試後改為手動重試；已成功 rank/explanation 不重付費。同一解釋不因 render/interim 重複插入。
9. 普通 lecture notes 與 Jev 可並行，普通筆記不用等所有搜尋。NoteAgent 每 8 秒檢查已確認來源，每次最多 40 transcript，新增教材最多五份；只有至少 40 個識別字元才生成。Gemini 經 Agent Framework `RunStreamingAsync`，Activity draft 每約 700 ms 被 UI 讀取，畫面清楚標為尚未保存。已確認 note 不清空，未保存 editor/caret 保留；讀者的文字 anchor 保留，原本已在尾部才跟隨 append。
10. 保存前檢查 term references、note base version 及 output language。若使用者／補充版本先保存，過期輸出被拒絕，來源保留待重試；不覆蓋新版本。真實驗收碰過這個競態，先記錄 failed，後續成功保存。歷史還原另建版本，不刪舊版本；還原後不自動重新插入同一已套用補充。

## Ask Playback

右下角 Send 會保留問題、停用重複送出，使用每次問題的 request ID。後端同 session／同 ID／同 payload 重用工作，最多保留 128 個 request cache entry；重啟後 cache 不存在，不能把它當永久 provider 去重。140 秒服務端、150 秒前端總 deadline；手動 retry 是新 request。

Lecture answer 只可引用本次提供的 session source ID；可接受 `[id1, id2]`，不接受陌生 ID。沒有來源、`INSUFFICIENT_SOURCE`、引用不通過或 lecture provider 失敗時，回傳清楚狀態。**已開啟 web 時，獨立 web 分支仍可繼續**。`ashion` / `What is cache` 真實案例已得到「課堂證據不足／修正只是假設」及獨立有來源的 cache 解釋；沒有捏造教授講过 cache。web 失敗也不丟棄有效 lecture answer。

NDJSON 提供真實生成中的 draft 和最後驗證答案；draft 不可引用為確認來源。timeout、截斷串流、provider error 會結束 loading 並保留問題；切換 session 會取消舊工作並拒絕舊答案。Google Search 回傳的 metadata／query／usage 在新結果中保存；早期結果只有 evidence/suggestions，沒有補造未記錄 token 或費用。

## Activity 與延遲／費用

Lecture notes 標題旁 ↶ 提供 hover、click、focus、Escape 和 touch。每 session 最近 100 條執行顯示 queued/running/completed/failed/cache-hit、provider/model、時間、耗時、source 和錯誤；同區沿用已保存 note edits／Jev decision。API 重啟留下的 running 紀錄會標為中斷失敗。沒有呼叫就不顯示模型正在生成；chunk 的 VAD/ASR 狀態仍在 transcript 區。Activity 沒有聲稱包含 provider 的內部推理或完整帳單。

REST 預覽會重送部分人聲，因此增加費用；兩秒不是固定實際 latency。首次 preview timeout、短分段吞字及 provider queue 都已在結果中明列。真實可見灰字一次約 3.46 秒，並非 SLA。Jev gate 可省掉不值得解釋的 Gemini/Search 呼叫；搜尋會增加延遲／費用，結果保存後重用。筆記有界批次減少呼叫，CAS 競態的失敗生成仍可能收費。

## 驗收及重跑

預設離線：

```powershell
dotnet build app-temp/checks/Playback.Checks.csproj --no-restore -p:OutputPath=bin/offline-checks/
dotnet app-temp/checks/bin/offline-checks/Playback.Checks.dll
npm.cmd --prefix app-temp/web run build
node app-temp/web/src/test/asr-ui-check.mjs
node app-temp/web/src/test/validation-replay.mjs
node app-temp/checks/validation-report.mjs
```

Replay 需要本機 Vite 5181 提供前端檔案；它攔截全部 API、封鎖外部 HTTPS，不依賴 live server。API integration/E2E 要以 `PLAYBACK_OFFLINE_TEST=yes`、`PLAYBACK_MONGO_DATABASE=playback_e2e`、`PLAYBACK_ASR_PROVIDER=openrouter`、`ASPNETCORE_ENVIRONMENT=Development` 啟動 API 5079 及 Vite 5174，再執行 `node app-temp/api/integration-check.mjs`、`node app-temp/web/src/test/e2e-check.mjs`。只有 E2E 自建測試 session 被清理，沒有清空 database。

付費 runner 必須明確 `--live`；同一已保存結果（包含失敗）預設重用，`--refresh` 才重送：`validation-asr.mjs`、`validation-app.mjs`、`validation-live.mjs`、`validation-capture.mjs`。首次 fixture 可用 `prepare-validation.ps1`（Windows 安裝的 Cantonese/English TTS）及 checks 的 `--validation-fixtures` 產生。自然課堂人工對齊未完成，不把合成文稿叫做人手聽寫 ground truth。

日常 API health 現包含 build MVID、executable、啟動時間及 capabilities。此次交接載入 `bin/current/net10.0/Playback.Api.dll`，已正常停止舊 paused recording，原始資料保留；逐 session 核實沒有待送舊 ASR 後，已啟用正常自動重試及 recovery，新錄音自動化正常。一般 `npm.cmd run dev` 亦使用正常 recovery 設定。

仍未完成：無直接 streaming provider key，沒有具體原生 streaming adapter；自然粵語準確度仍有已知錯詞；無三小時實機穩定性承諾、沒有講者辨識、沒有安全部署／登入功能。
