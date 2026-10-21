# Wave 1 收尾（2026-10-20）

**離線實作／文件已完成，live acceptance 未全部通過。** 本輪依使用者要求提交至本機 `main`，未 push、未部署、未建立雲端帳戶、未清原 MongoDB。下一個 session 優先討論 cloud deployment 同真正 accounts。

| 項目 | 結果 |
| --- | --- |
| P1 Web／實機 | Web build、全部指定 offline browser、實際 saved API desktop／375px／引用跳轉通過；首 note 132 秒未達 40 秒。遲到來源 placement 修正有 offline／小型 in-memory live 證據，原 Mongo 兩段仍 pending。[詳情](p1-results.zh-HK.md) |
| P2 replay | 合成廣東話兩模型八分鐘已跑；Gemini 長測109分鐘後停；Qwen 約133分鐘取消，in-memory note 未保存，完整雙模型未完成。P2 最後可見約 US$2.60594，非帳單；全部 paid runs 已停止。[數據與語義抽查](p2-results.zh-HK.md) |
| P3／M | time-full organization status projection 已補 source timing；review threshold 與 writer schema 已一致，有 regression checks |
| P4／P4b | 保留 separate requests、default GLM policy、45 秒 silence；P4b unique cost US$0.07850672，手動 recovery 後58/58；未證明連續長跑／GLM organizer 品質。[結果](p4b-results.zh-HK.md) |
| P5 | [PostgreSQL／R2／Cloud Run 分階段計劃](../../storage-migration-plan.zh-HK.md)已寫；未執行遷移 |
| P6 | [Diarization／文件抽取研究](p6-results.zh-HK.md)已寫；未安裝整合 |

最後 backend build 0 warnings/errors，完整 offline suite 通過；Mongo note history／CAS／Stop checks 只針對獨立 `127.0.0.1:27019/playback_e2e`。原 development DB／使用者 sessions 未清除。所有 live raw evidence 保留在 ignored `app-temp/data/validation/runs/2026-10-20-wave1-*`，唔會提交音訊或 credentials。

新增 runner 支援 raw/OpenCC、bounded duration、共同 budget admission、saved-results fingerprint、手動 recovery。唔將 synthetic fixture、cached report、手動補寫或 browser 截圖當成自然長課堂成功。

## 之後次序

1. 先用現有保存 replies／fixture 檢查 latency／late-ASR 修正，不新增 paid replay。P1 真實端到端與 Gemini 完整長測仍明示未驗收。
2. 依 P5 計劃處理 cloud／accounts：先確認 single-app target、browser capture（Linux Cloud Run 無 Windows WASAPI）、真正 login 方案／ownership、background execution 同費用界線，做可 review 嘅具體 diff／checks。
3. Deploy 前提供 service／region／image／branch／auth／storage／已知差異同驗證結果；依既有部署約定取得最後批准。若要清本機 DB，先列明目標、損失資料同 rebuild plan，再取得專項批准。
4. 不自動開始 meeting profiles、FAQ、材料上載或 diarization 實作。

曾提出提高長測預算，使用者質疑後已撤回；全部 paid runs 已停止。未來測試先從小段、保存 evidence 同 offline regression 開始。
