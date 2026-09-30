# Playback：ASR、即時筆記與 Jev 修復及驗收任務

請在 `C:\Users\peter\Documents\Github\playback` 實際完成以下修復。我需要可觀察、可實測的錄音及筆記流程，並說明 AI 的實際運作邏輯。先讀 `AGENTS.md`、`docs/business-requirements.md`、`docs/scope-decision.zh-HK.md`、`app-temp/README.zh-HK.md` 和相關程式／測試，再改 `app-temp/`。

若我的描述有歧義，先問精簡問題，同時繼續不依賴答案的排查。完成已授權的工作；需要真實 provider 的功能，必須以真實請求及結果驗收，清楚區分程式編譯、mock 測試、live API 測試及實機錄音。

## 已確認的需求與授權

- 主要錄音語言是 **廣東話及 English，包括中英夾雜**。ASR 應保留粵語口語、英文、縮寫及技術詞，不把粵語改写成普通話。
- **我選廣東話，就要繁體粵語口語＋英文。**「SC」是目前錯誤顯示的簡體，不是我要求的輸出。需查明 Qwen 原始 ASR 是否產生簡體、誤聽、改寫成普通話，及 app 的顯示轉換是否真正生效。可以換較好的模型，不需固守 Qwen。Notes output 的 TC／SC／EN、translation target 是獨立設定，不能與 ASR 語言混淆。
- Provider 優先 OpenRouter；如果它不提供適用的真實串流 ASR，**可以研究及比較直接 provider**。實際接入仍需有效憑證；不要把 OpenRouter key 當成其他 provider 的直接 key，不要開付費帳戶或訂閱。
- **已授權以現有 process environment 憑證、repo sample 和必要的合成素材執行有限的 live ASR、Jev、AI＋網絡搜尋及 chatbot 驗證**，包括測試所需的網絡／音訊上傳及可能的 API 費用。先說明小規模測試計畫和請求數，按已授權範圍執行，不重複詢問相同批准。保存結果，平常讀取已有結果；不要每次啟動、build 或 test 都重新呼叫付費 API。不要預設送完整 20 分鐘或大量重試。
- **Jev 選中術語後，自動使用 AI＋網絡搜尋產生解釋，加入筆記並突出術語。**簡短解釋可直接讀，詳解及來源可展開；變更可還原。不要等我按一下才開始解釋，也不要讓既有禁止解釋入筆記的 prompt 阻擋此需求。
- **右下角 Ask Playback chatbot 不 work，是本次必須修復並實測的功能**，不是只記錄或解釋截圖錯誤。
- 錄音計時暫按「本次 Record 開始後實際錄音的累計時間，排除 pause」；stop 後重新開始是新一次錄音。既有 session 音訊長度另計。如我的意思不同，確認後調整。

目前 HEAD 是 `6d0ae35`。之後有大量未提交的語言設定、短音檔預覽及串流接線修改，也有原本已修改的 `.env.example`。先檢查 status／diff，保存所有未提交工作，核對本機現況，不假定上一輪工作已全部提交或實測成功。不要自動 commit、push、清空資料庫或刪除我的資料。

應用／測試程式可以透過既有啟動方式把 `.env` 載入 process environment。只從 process environment 使用 keys，遵守 AGENTS.md；不要讀出、顯示、修改、記錄或提交 `.env`／secret。也不要修改原本的 `.env.example` WIP。

我會附上問題截圖。若截圖沒有跟新 session 一起提供，仍可按以下明確描述工作。

## 1. ASR 準確度、模型選擇與真實測試

現在 Qwen 的廣東話逐字稿很差、很慢；選廣東話後也出現錯誤字形。需要找出設定、provider、模型、音訊品質、分段及正在運行的版本各自造成的問題。

1. 用當前官方文件／模型目錄研究 OpenRouter 的 transcription 模型和 provider 能力：廣東話、英文、中英夾雜、language hint、原生串流輸入、partial output、timestamps、限制及計費。核實每個候選模型的實際 endpoint、payload、language 代碼及可用性。
2. 現有模型選單包含 `qwen/qwen3-asr-1.7b`、`openai/whisper-large-v3`、`openai/whisper-large-v3-turbo`。把它們當候選項，實測後才作選擇；亦可研究其他合適模型。選單只列已確認適用的 transcription 模型，不把一般 chat／audio-understanding 模型當成同一協議使用。
3. 我能在 UI 選不同 OpenRouter ASR model，設定按 session 保存、重載後保留，請求實際使用所選模型，逐字稿保存實際 provider／model。設定改變後的生效時點及 retry 行為要明確，不能畫面選 Whisper、實際仍送 Qwen。
4. 單一廣東話與混合廣東話／英文要有正確、已驗證的處理方式。不要假設通用 `language` 欄位一定接受或採用 `yue`。不支援的設定應顯示真實結果及限制。
5. 繁體顯示要覆蓋**灰字 interim 及正式逐字稿**，保留英文、粵語詞及引用。Raw ASR 保留供回聽與比較；字形轉換不可冒充準確度改善。檢查 session 保存、模型回應、converter、API、前端及舊／新錄音的完整路徑。
   - 在對照報告分别列出「人工 reference」「provider 原始文字」「app 顯示文字」，突出 script 差異、語義誤聽及粵語被普通話改寫，不能把它們合成一個錯字數。
   - `language=yue` 與「繁體字輸出」是兩個需分別驗證的行為。若 raw 是簡體但仍是正確粵語，驗證繁體轉換；若 raw 已誤聽／改寫，簡轉繁不構成修復，需比較其他模型的辨識品質。
   - 驗證自然粵語詞如「佢哋／我哋／唔係／喺／而家／呢度」及夾雜 English technical words；不能只檢查畫面有幾個繁體字。
   - 選廣東話後，也檢查既有簡體逐字稿的顯示。可按明確的顯示偏好轉為繁體，但保留 provider 原文、來源及版本；單純更改顯示不能觸發舊音訊重新上傳或假裝重新辨識。
6. 用同一組有人工核對文字的片段比較模型：廣東話、英文、中英夾雜、技術詞，例如 `cache`，並測靜音／背景聲。Repo sample 在 `app-temp/data/test-audio/sampleAudio/`；音檔只有首 20 分鐘，`transcript.txt` 是完整講課稿，需先對齊片段，不能直接拿整份當短片段 ground truth。
7. 授權後跑真實 API，再跑 application endpoint，再驗證 UI 到結果的流程。報告模型／provider／語言設定、音訊長度、第一個 interim 延遲、final 延遲、錯字／漏字／幻覺例子、英文技術詞表現、實際 usage／cost（provider 有回報才列）、失敗及 timeout。CER／WER 或人工比對要說明 normalization 和樣本限制。

驗收：用實測支持推薦；真實 key、endpoint、所選模型及字形確實生效。若缺權限、key 無效或模型不可用，提供脫敏錯誤與影響範圍，不以 mock 結果宣稱 live 成功。

### 保存及重用實測結果，不每次重跑

固定使用 `app-temp/data/validation/` 保存本機驗證資料；此目錄的 `README.md` 和 `index.md` 已建立，只有交接说明，**尚未產生本輪 live 結果**。沿用此資料夾，不能把新結果只留在 terminal 或暫存資料夾。

- `index.md`：突出推薦模型、已通過項目、失敗、限制、測試日期、是否已過期及指向實際結果的連結。
- `fixtures/`：可重用的短音訊／ground truth／測試問題，保留與 repo 原始素材的時間及 hash 對應。
- `runs/<run-id>/`：manifest、脫敏的 ASR／Jev／chatbot 結果、latency／usage、錯詞對照、screenshots／必要短錄屏、可讀的 HTML 或 Markdown 比較報告。原始回應也需刪除 credential／authorization 等敏感欄位。
- 保存的 benchmark fingerprint 包含 audio hash、model／provider／endpoint、language、VAD／分段及正規化配置、測試案例及程式版本；metadata 不含 key。
- 平常測試預設離線，展示／驗證已保存結果。Live runner 需明確命令／選項，如 `--live`；相同 fingerprint 的結果可重用，要重新發付費請求需顯式 `--refresh` 或選定新案例。失败重試也要有限，不能默默每次重跑。
- Cached report 明示原始測試時間與測試版本；新程式的 UI／offline checks 和舊 provider 結果分開，不能讀舊結果便宣稱新版本剛完成 live 測試。Provider 或模型更改後的可用性需要重新核實時，說明理由並只重跑受影響項目。
- 資料夾及報告長期保留供我查看，highlight 出最好／最差結果及重要差異，不自動清除。不要提交音訊、課堂逐字稿或 raw provider 回應。

## 2. VAD 後持續 ASR 與穩定 interim 文字

我要語音經 VAD 篩選後持續送 ASR，邊講邊顯示灰字。**真正 streaming 不應等固定 8 秒才送；只有 REST 路徑需要音檔分段。**

- 分清楚「串流送入音訊並回傳 partial」和「完整音檔上傳後才串流輸出文字」。HTTP streaming、模型能力標籤或介面定義不能取代這項驗證。
- 選定 provider 支援原生 streaming 時，實作它的具體 adapter，使用官方協議、小音訊 frame、每 source 的 ordered stream、partial revisions、final／flush、取消、斷線及恢復；確實接到 capture 及 UI。
- 如果 OpenRouter 實際沒有此能力，說明研究結果。依我的 provider 選擇，使用直接 provider 的串流，或明確採用 REST fallback。UI 要顯示目前實際模式。
- VAD 用於網絡 ASR 的送音策略：合理 pre-roll、speech onset、短停頓 hangover、utterance end、最大緩衝及 backpressure，避免截掉第一個字、字尾、英文詞或中英切換。不要只以 `hasSound=true` 決定把包含大段靜音的累積音訊重複送出。
- 原始錄音照常保存、來源分離、時間對齊、可回聽／重試。辨識流及可恢復的音檔保存邊界可以分開，存檔分段不能阻擋即時送音。
- REST fallback 應用 VAD utterance boundary 和受限最長片段；研究適當的上下文／overlap、去重及費用。實測選擇分段策略，不能只把 30 秒改成 8 秒便宣告延遲修復。
- 有真實文字後持續保留、修訂灰字；下一個 frame、分段輪轉或較慢 final 請求不能令已有文字突然消失。正式結果替換對應範圍，避免重複或把不同 source／session 的文字拼錯。
- Interim 是假設，不作已確認來源；如要供 AI 提早處理，必須標記 provisional，final 到達後可修訂／撤回，不冒充可引用事實。
- 慢網絡、timeout、断線時保持保存及可見狀態，取消過期預覽，限制並行和積壓；不要把每次重複上傳都列為速度改善。

現有 `IStreamingAsrAdapter`／`LiveAsrSession` 有接線和 mock 檢查，但 `AsrAdapters.Create()` 目前只建立 OpenRouter REST 或 SenseVoice REST adapter。下一輪需驗證具體 provider，不能把介面接線當作真實串流已完成。

## 3. Record 顯示錄音經過時間

- 目前 `PlaybackHeader.tsx` 顯示下一段封存倒數，這不是我要的錄音時間。
- 改為本次錄音 elapsed time：`00:01 → 00:02 → …`；超過一小時正確顯示時分秒，不隨分段輪轉歸零。
- Pause 凍結；resume 接續；page reload／導航後仍以後端錄音狀態恢復，不能重新從零開始或計入 pause。
- 使用可靠 elapsed clock，區分錄音位移、session 已有音訊長度、wall-clock timestamp 及本次 recording duration。計時不能依賴 ASR 回應。
- 驗證開始、跨分段、pause／resume、stop／restart、重載及一小時以上的顯示。

## 4. Activity 搬到 Take Note 的標題旁

- 將 Transcript 的 `Activity` tab 移到 Take Note／Lecture notes 區域。
- 在 **Lecture notes 標題附近**加入 history icon；hover 顯示小型矩形 popover，展示 AI 模型和 Jev 的活動及呼叫紀錄。
- Hover 到 popover 內容時仍能查看；同時支援 click、keyboard focus、Escape 和觸控，不做只能 hover 的介面。
- 顯示實際 queued／running／completed／failed／retrying／skipped／cache-hit 等狀態，讓我知道「已做甚麼、正在做甚麼、為何等待／跳過」。包括任務名稱、provider／model、時間、耗時、來源範圍、結果摘要與脫敏錯誤。
- LLM note revision、Jev ranking、term explanation、translation、Ask Playback 能分辨；沿用適當的既有版本／決策紀錄，補足目前缺少的執行中及失败狀態。避免另起一套互相矛盾的 activity source of truth。
- 記錄按 session 隔離，限量／可捲動，不能讓三小時錄音的 popup 無限變大；保留來源及 note version 的查閱能力。
- 不能把一筆已保存 note version 假裝成整段期間所有模型呼叫的完整 log；也不能只因有 key 就顯示 provider 已驗證可用。

## 5. Jev、AI loading 與筆記 live edit

### Jev 必須真的參與術語決策

先追蹤完整路徑：final transcript／教材 → candidate extraction → Jev request → ranking／是否需解釋 → highlight／explanation／note revision。使用來源上下文判斷 technical term，保留原詞、來源及不確定性。

目前值得排查的已知程式行為：

- `TermCandidateExtractor` 主要找大寫縮寫及多字首字母大寫詞，可能漏掉 `cache` 等小寫單字和中文技術詞。
- `TermReviewAgent` 自動處理須 `PLAYBACK_AUTO_TERMS=yes`；每兩分鐘合計最多處理 3 個候選詞，可能造成等待／積壓。先檢查實際有效配置及正在運行的版本。
- Jev ranking 目前只把 term 當 `state`，要確認上下文是否足以辨別技術／日常用途。
- `ShouldHighlight` 目前用 explain probability、category confidence 和 high／medium rank；檢查實際 model schema、數值和閾值是否符合需求，不把本機大寫／長度規則當成 Jev 決策。
- `NoteAgent` 目前指示不把補充解釋寫進 note body，與已確認的自動解釋入筆記行為衝突，必須修改相關 prompt／呼叫流程並驗收。

既有 `/api/terms/rank`、`/api/sessions/{id}/terms/review`、term explain endpoints 及 `npm.cmd run test:jev-live` 應先檢查並復用。現有 live 檢查主要是直接 classifier 的 `spectrogram`／cache 測試，需補足 application endpoint、實際自動觸發和 UI 的端到端驗證。

使用已授權的實際 `JEV_API_KEY` 驗證 model discovery、rank schema、probability／confidence、cache、失敗及 timeout。測試 `cache`、`FFT`、`spectrogram`、中文技術詞、中英夾雜及普通詞；結果與 contextual technical/nontechnical 標註比對並保存。若 401／403、沒有可用 model、schema 不符或服務不可達，Activity 要明示真實問題。

Jev 決定術語值得解釋後，**自動觸發 AI＋網絡搜尋**，用課堂／教材上下文限定術語意思，生成可加入筆記的簡短解釋、突出術語及可展開的詳解／來源。說明 Jev 是決策模型、哪個 AI 模型生成解釋、網絡搜尋實際是否執行／有回傳證據。

- 補充解釋標示為「AI／網絡補充」，保留真實 web citations，不寫成教授曾經講過的原話，不偽造 lecture citations。
- 自動加入後可還原、來源可追溯，既有 user edit 不被覆寫；不對每次 interim 或每次 render 重複新增同一解釋。
- 按術語、上下文／意義、語言及解釋版本保存、去重及重用解釋和搜尋結果；相同詞的不同意思不能共用錯誤答案。只有有關內容／配置變更或我要求刷新才重新生成。
- 搜尋或模型失敗時顯示失敗／重試狀態，不偽造已搜到的來源，也不要阻擋所有普通筆記更新。

### 真實處理狀態與流暢筆記

- 音訊到達後立即顯示對應處理動畫／狀態：capturing／VAD、ASR、等待確認文字、Jev／notes queued、模型正在生成、完成或失敗。不能只有按「Revise with AI」才有 loading，也不能在沒有模型呼叫時顯示模型正在工作。
- 語音及已確認逐字稿到達後能及時更新筆記；研究 event／queue + bounded debounce／batching，避免只能等目前兩分鐘掃描才反應，也避免每個 PCM frame 都呼叫 LLM。
- 若所選 note model 支援 streaming output，接入實際 token／block updates，兼顧未完成 Markdown、引用驗證及取消。Interim note 與保存版本的關係要清楚。
- 使用現有 React 的穩定節點／key、按內容 block 更新及必要的 memoization 等方式，保持未改內容、選取、editor caret、手動未保存內容及 scroll anchor。不要為「像 virtual DOM」而新增一套重複框架。
- 筆記變更只更新受影響內容，避免整個 notes panel 重建、空白後重畫、screen 抖動或捲回頂部。讀舊內容時保留位置；已在尾部跟隨時才跟隨新增内容。
- 新舊 ASR、Jev、note results 的競態要處理，避免舊 session、舊語言／模型或舊 note version 的晚到結果覆寫新狀態。
- 筆記仍是 presentation／lecture notes：段落為主；flowchart、list、tree、table、code block 只在有助理解且有來源時使用。誤聽的語音要保留疑問，不生成流暢但虛構的課堂內容。

請最後用具體例子說明 AI 邏輯：收到一段「我哋而家講 cache」後，VAD、ASR、interim/final、candidate extraction、Jev、explanation、note revision、引用及 UI 狀態各自做甚麼；甚麼條件跳過、重試、去重、等待或保存。也說明哪些階段用何種模型、並行／依賴關係、對延遲及費用的影響。

## 6. 修復右下角 Ask Playback chatbot 的完整流程

我確認右下角 chatbot 不工作；本次必須修復並完成真實端到端驗收，不只處理一次特定 citation error。

- 排查輸入／送出、選中 transcript 的 context、問題內容、API request、有效 key／model、回應解析、web search 分支、citations 及前端顯示。
- 無選取時能問已處理的 session；選取文字時正確帶入其上下文。使用者問題與誤聽的選取詞不一致時，處理不確定性，不能把問題忽略或盲信誤聽詞。
- Send 有真實 loading／streaming（model 支援時）、成功及可理解的失敗狀態；處理 timeout／retry，不重複發同一請求。不得永遠轉圈、空白或因單一引用格式問題阻斷全部已允許的 web 回答。
- 驗證 public web search 關／開、lecture 有證據／無足夠證據、session 隔離、正確 lecture links／web links、後端錯誤、重複送出及窄屏 UI。
- 使用已授權的 AI／網絡搜尋真實請求，並保存最少必要的測試問題、上下文來源 ID、脫敏回應、引用及可見畫面到 validation 資料夾；日常重跑讀取 fixture，不每次重新呼叫 API。

### 截圖中的可重現案例

截圖中已勾選 public web search，選中的 ASR 文字是 `ashion`，問題是 `What is cache`，卻顯示 `AI answer did not cite a supplied source`。請調查並修復此可見問題。

目前 `ChatAgent` 先要求 lecture answer 通過來源引用驗證，再執行 web search；前一步失敗可能令已啟用的 web search 根本不執行。需要驗證真實觸發路徑。

沒有足夠課堂證據時，清楚說明來源不足；有啟用 web 時可提供獨立、有可驗證 web citations 的概念解釋。`cache` 是對誤聽詞的推測時要標示推測／向我確認，不能宣稱課堂明確講了它。修復可靠的來源策略及 graceful response，不移除 citation validation，也不偽造 lecture source IDs。

## 驗收與交付

1. 先排查是否仍運行舊 API／舊 binary：截圖仍有約 30 秒 transcript 範圍。以 health、啟動路徑、實際 capability／版本和 readback 驗證 API、UI 及最新程式一致。若正在錄音，先保護音訊及說明所需重啟，不能直接殺掉錄音程序。
2. 同一真實測試 session 完成：選模型／語言 → 錄音／重播授權樣本 → VAD → 灰字 → final → Jev decision → 流暢 note revision → history popover → 有來源的 Ask Playback。證明各階段確實觸發，包含失敗路径。
3. 驗證 timer、pause／resume／reload、source 分離、session 隔離、語言保存、interim 修訂／final 去重、retry idempotency、網絡失敗時音訊保存、Jev endpoint、Activity 實時狀態及 notes scroll／caret 不跳動。
4. 瀏覽器檢查桌面及窄屏，保存真正可見的灰字、popover、loading、逐步 note 更新及錯誤狀態截圖／短錄屏。不能只測 locator 存在而內容在螢幕外。
5. 更新相關 README、測試及 AI 流程說明。完成 build／必要 checks，提供實際執行過的命令、結果及可重現操作，並提供 `app-temp/data/validation/index.md` 的結果索引；展示報告時不重新呼叫 API。
6. 最終交付說明每項已修復行為、所選／已測模型、live 測試數據、AI 邏輯、未解決問題和授權／provider 阻礙。未測的準確度、延遲、硬件串流或三小時穩定性要明列，不能宣稱完成。

不要停在模型研究、修改 prompt、縮短分段、增加 interface 或跑 mock。把已授權的修復和對應驗收做完；若實際服務條件阻止完成，提供可檢查的證據與仍需解決的具體事項。
