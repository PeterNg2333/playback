# 已封存的 Electron 錄音實驗

這是早期 desktop capture 候選，現已退出預設啟動流程。實機無法開始錄音的問題尚未解決；不要把它當成可用的錄音方式或產品方向。

程式碼保留供日後研究 Windows system audio、mic 與本機持久化 queue。執行時產生的音訊及 Electron cache 仍在 `app-temp/data/desktop/`，不隨程式碼移動，並已加入 `.gitignore`。目前 `pnpm.cmd dev` 只啟動 .NET API 與網頁 UI。
