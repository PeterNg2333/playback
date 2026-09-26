# ASR + AI 筆記工具：範圍決策

## 目的

- 協助學生及會議參與者即時掌握重點，並在事後查找原話與筆記。
- 支援約 3 小時的課堂或會議；錄音與網絡中斷時，已保存的音訊可補傳及重跑。

## 功能

- Desktop 錄全機播放聲音；手機只錄外置／內置咪高峰。兩者均支援本機分段保存與斷線補傳。

## AI 功能

- **ASR＋翻譯**：SenseVoice 即時產生原文逐字稿；Gemini 翻譯及提出不確定字詞的修訂建議，保留原文供核對。
- **術語與解釋提示**：從教材與逐字稿找出專有名詞；Jev 作術語排序及「是否建議更多解釋」的候選模型，由 Gemini 生成可展開的解釋。
- **教材＋錄音筆記**：Gemini 持續整理逐字稿與講義／投影片，生成課堂筆記；可像 NotebookLM 般按來源提問，答案連回教材或錄音位置。
- **即時私人提問**：課堂進行時可就已轉寫內容向 AI 發問；每人的問題與回答只供本人查看。

## 現階段決策

- 先在本機完成 `scripts/` 可行性研究；暫不建立正式 client、API server 或雲端服務。之後在`app-temp/` folder 做.net 10 + React + Vite 的 prototype，並驗證 desktop 與 mobile 的收音可行性。 (先以我個人能測試的環境為主，之後再找同學測試)
- 未來產品的 Web UI 採 React + Vite；audio API 與 AI 處理採 .NET 10；資料庫方向改為 MongoDB（目前先local monogoDB，之後會用cloud monogodb altas + google bucket）。
- Desktop client 以 Electron + TypeScript 為候選，支援 Windows 與 macOS；兩邊收音可行性仍待驗證。
- 手機先做 UI；若要可靠錄約 3 小時及支援切 app／鎖屏，規劃 native mobile client。

## 未來計劃

- 一位學生發起課堂錄音；其他 client 可接力補錄。獲授權的同學可共享逐字稿與筆記，個人 AI 對話保持私人；收費與分享權限仍待研究。
- .NET API 部署到 Cloud Run；audio 放 Cloud Storage，session、逐字稿及筆記放 MongoDB。
