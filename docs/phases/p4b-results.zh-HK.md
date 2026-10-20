# P4b：GLM fallback 決定同實測

2026-10-20。使用者明確決定：保留逐個 model request、GLM 預設 data policy、45 秒 stream silence timeout；批准比較，原 US$0.05 上限後提高至 **全部 P4b 合計 US$0.10**。

## 決定落實

- 不改 production routing：Qwen（none）→ GLM（low）→ Qwen → Gemini；各自一個 request，因 reasoning 設定不同。
- 不加 `data_collection: deny` 或 `zdr: true`。預設 provider policy 不等於零保留／不訓練承諾；本次實際 serving providers 係 Together 同 BaseTen，資料經 OpenRouter 送去上游。
- 維持 45 秒資料靜默／100 秒完整 call deadline。已有 in-memory SSE、cancel、stall、fallback checks；今次無改常數。
- 比較 runner 加 OpenCC option，同保存 raw replies、saved note、逐 call usage/cache、budget admission。詳見 P2 結果。

## Live 結果同邊界

全部係 repo Week 3 文字經 production `NoteWorkflow`、in-memory store；冇 ASR／新音訊上傳。Prompt `tree-notes-v15`／`tree-organize-v19`；之後 P1 嘅 v16 prompt 修正唔在呢批 benchmark 內。

| Run folder（`app-temp/data/validation/runs/`） | 結果 | 費用 US$ |
| --- | --- | --- |
| `2026-10-20-wave1-p4b-glm` | guard 喺約 15 分鐘停；20 成功 writer、39/41 已到達來源有引用 | 0.0298852 |
| `2026-10-20-wave1-p4b-glm-final` | guard 喺 18:50 停；25 成功 writer、51/53 已到達來源有引用；1 organizer call | 0.04560662 |
| `2026-10-20-wave1-p4b-glm-recovery` | 載入上一份 notes／activity，用 1 次手動 Generate 補其餘來源；58/58，有保留原失敗紀錄 | 新增 0.0030149 |
| **實際合計** | 保留三份證據，唔重複計 recovery 匯入嘅舊 usage | **0.07850672** |

Budget guard 預留「下一個 request 可能用到最大 tokens」嘅費用，所以會喺實際支出低於 cap 時停止。Local budget refusal 唔係 GLM HTTP failure。Recovery 係手動補寫，**唔係無中斷跑完 20 分鐘 batch schedule**；唔將 58/58 當成 uninterrupted reliability 證據。

最後合併證據：26 成功 writer + 1 個本地 budget refusal；writer median/max 3,417/6,167 ms；278,788 input tokens，4,864 cached，14,774 output；11 topics、56 points、0 empty topics、1 個短 placeholder heuristic 命中（其引用亦喺其他實質段落，sources-only-in-placeholder=0）。Saved note 嘅 common Simplified-only 字元 heuristic 命中 0，唔代表做過所有 Unicode 異體字檢查。

Recovery 新 request 嘅 raw reply 相同 heuristic 亦為 0；此檔案只保存 recovery request，唔包含匯入舊 activity 所對應嘅全部 raw replies，不能同最終合併 note 做 token-for-token 比較。

Organizer 只有一次，而且冇成功改結構：8 個範圍外 operations、2 個超過三層深度 moves 被丟棄。呢次證明 malformed organization 保留原文，**未證明 GLM organizer 有足夠品質**。同 P4 舊 Qwen run 58/58、median 11.6 秒、US$0.0311 相比，GLM writer 較快；費用／分組唔可當完全相同 cadence 嘅公平 ranking。

結論：可保留 GLM 作 transient transport error 嘅第二層 writer，但持續長課堂及 organizer 品質仍有以上限制。唔新增「任何 parse error 都自動用另一個模型再寫」嘅行為；合約錯誤仍照實顯示。

## 驗證

完整 offline backend suite 通過；build 通過，0 warnings／errors。重現指令（再次執行會收費，必須另有當次授權）：

```powershell
node --env-file-if-exists=.env app-temp/checks/Validation/run-note-models.mjs --live --models 'openrouter:z-ai/glm-5.3-flash@low|batch+organize' --script zh-Hant --max-usd 0.07 --folder app-temp/data/validation/runs/<new-folder>
```

Public endpoints metadata 已於 2026-10-20 查閱：[GLM](https://openrouter.ai/api/v1/models/z-ai/glm-5.3-flash/endpoints)、[Qwen](https://openrouter.ai/api/v1/models/qwen/qwen3.8-flash/endpoints)。GLM routing 仍用 production `require_parameters`／quantization 條件，冇放寬為 fp4；未知 quantization 仍係現有設定容許嘅候選，唔把 unknown 當作已證實 fp8。

範圍外改動：比較 runner、offline runner checks 同 Program 登記屬 P2／M，一併列於 Wave 1 收尾，本輪一併提交至 main，未 push。
