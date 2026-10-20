# P6：講者分離候選同接入方式

查閱日期 2026-10-20。現有 microphone／system 係音訊來源，唔係真人講者。以下係研究，未上傳新 diarization 音訊、未建立 provider account、未驗證廣東話講者準確度。

## 候選比較

| 候選／版本線 | 廣東話／混合語言 | 即時／講者數 | 時間格式同 chunk 接入 | 價錢／資料去向 |
| --- | --- | --- | --- | --- |
| Azure Speech `ConversationTranscriber`；SDK API 以官方 quickstart 為準，實作時固定 package patch | Speech language 表列 `zh-HK`；混合英粵實際準確度另測，唔由 locale 支援推論 code-switch 品質 | 有即時 speaker labels／intermediate opt-in；speaker ID 初期可以 Unknown；唔承諾未核對嘅固定上限 | SDK offset/duration 跟所送 stream，轉成 session ms；同一 source 需持續 session，唔每 8 秒重新開 speaker identity | 付費 Speech resource；送音訊到所選 Azure region；費率以 region/SKU quote 確認 |
| pyannote `speaker-diarization-community-1` + 現有 ASR | 只判聲音講者，唔翻譯；語言無關係模型設計特性，唔等於粵語已驗收 | 預設整檔事後；可給 exact/min/max speaker hint，冇聲稱通用固定人數保證 | 回傳 seconds+speaker turns；乘 1000 加 window start；重疊窗口需跨窗口對齊身份 | 本機 compute／儲存成本；模型下載要 HF 條款／token，下載後可離線跑；Python／PyTorch，唔係現成 .NET adapter |
| sherpa-onnx offline diarization（1.3 docs 線） | 組合 segmentation + speaker embedding；廣東話品質未驗證 | 官方頁為 offline 路徑；speaker count／clustering 依具體模型設定 | 本機 ONNX 結果轉 interval；可接 .NET wrapper，但需小 prototype 驗證 native runtime、模型同 API | 音訊可留本機；模型權利同 runtime license 分開核對；唔當作已安裝可用 |
| 目前 OpenRouter STT adapter | 目前文字／語言提示；無已驗證 diarization contract | 唔因 model 支援 audio 就假定提供 speaker labels | 現有 chunk identity 可保留，但 parser 未保存 speaker turns；要先確認指定 model endpoint schema | OpenRouter 再路由上游 provider；唔只係送到 OpenRouter；依 model/provider 價格 |

Azure 來源：[即時 diarization quickstart](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/get-started-stt-diarization)、[LID／diarization config](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/configure-language-identification-diarization)、[language support](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/language-support?tabs=stt)、[官方 pricing](https://azure.microsoft.com/en-us/pricing/details/cognitive-services/speech-services/)。Generic speaker ID 唔等於已識別真人，唔可以直接當帳戶 identity。

pyannote 來源：[model card／CC-BY-4.0](https://huggingface.co/pyannote/speaker-diarization-community-1)、[toolkit／Python setup](https://github.com/pyannote/pyannote-audio)。Pipeline 接受 mono 16 kHz，輸出 diarization；exclusive turns 有助 transcript 對齊。開源 toolkit 同模型嘅 license 要分開記錄，部署需保留 attribution；音訊唔需要送去 HF 推論。

sherpa 候選資料：[官方 speaker diarization](https://k2-fsa.github.io/sherpa/onnx/speaker-diarization/index.html)。此份只確認 offline 功能路徑，未有足夠證據承諾某 .NET package／模型組合可直接上 production；實作前鎖 runtime/model version，記錄模型 hash 同授權。

OpenRouter 來源：[官方 STT 文件 repo](https://github.com/OpenRouterTeam/docs/blob/main/guides/overview/multimodal/stt.mdx)。現有 `OpenRouterAsrClient.ParseResponse` 只讀 text/duration/language/usage；provider 新增欄位亦唔會自動變成 Playback speaker 支援。

## 建議同後備

P13 第一個小 prototype 建議用 Azure `ConversationTranscriber`，原因係有官方 .NET 及即時講者事件路徑，符合會議即時顯示目標。先用固定 `zh-HK`，再比較英粵混合候選；其準確度、成本同 speaker 上限未驗收前唔取代現有 ASR。

後備係 pyannote community-1 事後標記：保留現有 ASR 原文，只新增 speaker intervals；避免一次過換 ASR 同 speaker 演算法。接受 Python worker 同本機 compute 才採用；若需要純 .NET，再做 sherpa-onnx 小 prototype，而唔強行將 PyTorch 塞入現有 API process。

## P13 資料合約同 recovery

保存 `sessionId, sourceId, streamId, windowStartMs, windowEndMs, audioHash, modelVersion, providerJobId, rawSpeakerId, startMs, endMs, confidence?`。原 chunk 用 session+source+sequence+hash 保持冪等；speaker result 另有 window/model identity，重試同一份音訊不重複加 turns。Provider job 同 session 重啟後 raw speaker IDs 唔保證一樣，唔可直接用 `Speaker 1` 跨 window 當同一人。

另設 session-scoped stable speaker ID 同 user label；例如 speaker-a → 講者甲。命名唔改原 ASR、raw diarization 或 source。跨 window matching 可留 Unknown／需 review，唔假裝 embedding 一定對齊。Pause／source切換各自有邊界；microphone 同 system 兩條來源唔混成一條音軌來「去重」。

Transcript 每條仍指返原 chunk/time，speaker spans 可覆蓋一條 transcript 嘅不同部分；唔因講者切換改寫原文。Notes 引用已有 speaker 命名時才顯示；私人 Ask owner 同 speaker identity 完全分開。

## Live 驗收計劃（今輪未授權／未執行）

先選 repo Week 3 sample 00:00–03:00 做一次 Azure stream、一次本機 pyannote 比較，保存相同 mono PCM hash、手工標註 speaker turns，同量 latency／speaker error／Unknown比例；Week 3 多為單一講者，呢個樣本只能驗單講者穩定性，唔足以驗多人重疊。之後要一段有真實兩人輪流／重疊嘅廣東話樣本，合成文字唔能驗 speaker 品質。

請求數：一次 Azure session（網絡斷線最多一次明示重試），一次本機 job；三分鐘即 0.05 audio hour，預算公式 `0.05 × 該 region/SKU 每小時單價` 加適用 diarization 費。具體單價同 account region 開工時查核，先訂費用上限才送；無 account/quote 唔填假精確數字。音訊上傳授權由當次 live test 取得。

完成條件：speaker rename/reload、chunk retry、window restart identity、Unknown、overlap、兩個來源分別、跨 session 拒絕各有 offline check；browser 會顯示 Unknown 同原始來源，唔把缺少 labels 當成功。
