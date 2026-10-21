# Concept graph sidecar（LightRAG）

可選功能。每個 session 有一個 [LightRAG](https://github.com/HKUDS/LightRAG) workspace，由 Gemini 從逐字稿抽取概念（entities）和關係（relations）。用途：

- **Ask**：先由概念圖揀相關段落，再按原本方式交 Gemini 回答及引用；圖關閉、冇結果或失敗時用原本的關鍵字檢索。Activity 會有一條「Concept graph query」記錄實際用咗邊條路。
- **概念查閱**：`GET /api/sessions/{id}/concepts?q=…`（冇 `q` 就係整個圖），每份文件對應返原本逐字稿 ID。

佢**唔會**用嚟決定改咗逐字稿之後邊啲筆記要更新；嗰部分用 lineage（`domain/Evidence/Lineage.cs`），係確定性嘅。

## 文件單位

每個音源每一分鐘講課一份文件（`{session}-concept-{source}-{minute}`），內容係嗰分鐘開始嘅有效逐字稿（rerun 之後即係新 segment）。錄音中只索引已結束嘅分鐘。Session rerun 改咗某分鐘嘅文字，hash 就變，sidecar 會 `adelete_by_doc_id` 再 `ainsert`，LightRAG 只重建受影響嘅 entities／relations。Hash 相同就唔再送，sidecar 亦會略過。

## 費用與私隱

每份新或改咗嘅文件都會用 Gemini（`gemini-3.1-flash-lite`）做 entity／relation 抽取，再用 `gemini-embedding-001` 做 embedding；每次 Ask 亦會多一次 keyword 抽取及 embedding。三小時講課約 180 份文件。未做 live 量度，費用未知。逐字稿文字會送去 Vertex AI（同 API 現有 Gemini 呼叫一樣）。

## 安裝（需要明確同意，會下載套件）

需要 Python 3.10+。只裝喺 `app-temp/graph/.venv`：

```powershell
py -3.12 -m venv app-temp/graph/.venv
app-temp/graph/.venv/Scripts/python.exe -m pip install -r app-temp/graph/requirements.txt
```

LightRAG 第一次切 token 時，tiktoken 會下載 encoding 檔；之後會快取。

## 啟動

`.env` 或環境變數加 `PLAYBACK_CONCEPT_GRAPH=yes`，再 `pnpm.cmd dev`。`dev.mjs` 見到 `.venv` 先會啟動 sidecar（`127.0.0.1:5090`，可用 `PLAYBACK_GRAPH_PORT` 改）。Sidecar 由 process environment 讀 `GOOGLE_AI_STUDIO_API_KEY`，用 Vertex AI express mode（同 API 一樣）；LightRAG 內置 Gemini binding 喺 Vertex 模式需要 GCP project，所以 `server.py` 自己提供 completion／embedding function。

| 變數 | 預設 |
| --- | --- |
| `PLAYBACK_CONCEPT_GRAPH` | 唔設定即關閉 |
| `PLAYBACK_GRAPH_PORT` | `5090` |
| `PLAYBACK_GRAPH_MODEL` | `gemini-3.1-flash-lite`（只接受 `gemini-3.1-flash-lite`／`gemini-3.5-flash-lite`） |
| `PLAYBACK_GRAPH_EMBED_MODEL` | `gemini-embedding-001` |
| `PLAYBACK_GRAPH_DIR` | `app-temp/data/graph`（已 git-ignore） |

舊 session 要建圖：`POST /api/sessions/{id}/concepts/index`。刪除 session 會一併刪除 `data/graph/{id}`。

## 界線

- Sidecar 只聽 loopback，拒絕非 127.0.0.1 請求，request body 上限 2 MB；.NET client 固定 endpoint、唔跟 redirect、回應上限 2 MB、離線測試 (`PLAYBACK_OFFLINE_TEST=yes`) 拒絕。
- 用 LightRAG 預設本機檔案儲存（JSON KV、NanoVectorDB、NetworkX）。本機 `mongo:8.0` 冇 Atlas Vector Search，而 LightRAG 嘅 `MongoVectorDBStorage` 需要佢。
- **未做 live 驗證**：Vertex express 是否接受 `gemini-embedding-001`、抽取質素、時間及費用都要用 Week 3 sample 實測先可以講。
