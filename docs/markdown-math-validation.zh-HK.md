# Markdown 數學顯示與記憶體驗證

筆記原本以純文字顯示 `$p$`、`$a^{p-1} \equiv 1 \pmod{p}$` 等公式。現在沿用共用的 React Markdown renderer，加入 `remark-math`；KaTeX 只在遇到公式時載入，輸出原生 MathML。現有 Mermaid／flowchart、表格、來源引用和 Reading 模式繼續使用同一個 Markdown 流程。

## 支援格式

- 行內公式：`$a^{p-1} \equiv 1 \pmod{p}$`。
- 獨立公式：在公式前後各用一行 `$$`。
- `math` fenced code block：作獨立公式顯示。
- 普通 inline code／其他 fenced code 保留原文；金額可用 `\$5` 避免當成公式。
- 分數、求和和矩陣可放在段落或表格內。編輯內容不會被渲染結果覆寫。

不完整、不支援、過長或載入失敗的公式會顯示原始 TeX。渲染不會觸發 AI、儲存筆記或外部內容載入。

## 記憶體與渲染限制

- KaTeX 沿用 Mermaid 原本使用的 `0.16.47`，提升為明確的直接依賴，沒有第二份數學引擎。
- `output: "mathml"` 只產生一份方程結構，沒有另附 HTML 排版副本或下載 KaTeX 字型。字型由瀏覽器提供，Windows 使用 Cambria Math。
- 每個已掛載公式只保留自己的結果；沒有全域公式 cache。離開 Preview 或切換 session 後，公式元件會卸載。
- 每個公式限制 4,096 個字元、1,000 次 macro expansion、128,000 個字元的渲染輸出；`maxSize: 20` 限制尺寸指令。錯誤會保留來源。
- 一般 Markdown 先經 `rehype-sanitize`，只額外允許數學 code class。公式使用 `trust: false`，產生的 MathML 再經 DOMPurify。

原生 MathML 需要支援它的瀏覽器。本次以 Windows Edge 154 驗證；其他瀏覽器及作業系統未作視覺驗證。這個選擇依據 [KaTeX rendering options](https://katex.org/docs/options.html)，並保留 [rehype-sanitize 的 math class allowlist](https://github.com/rehypejs/rehype-sanitize#math)，沒有開放任意 HTML／style。

## 離線驗證

在 `app-temp/web` 建立 production bundle：

```powershell
npm.cmd run build -- --outDir output/playwright/markdown-current
```

在另一個終端啟動這份 bundle：

```powershell
node node_modules/vite/bin/vite.js preview --host 127.0.0.1 --port 5180 --strictPort --outDir output/playwright/markdown-current --configLoader runner
```

再執行瀏覽器檢查：

```powershell
$env:MATH_RUN_LABEL = 'after'
npm.cmd run test:math
$env:PLAYBACK_WEB_TEST_URL = 'http://127.0.0.1:5180'
node src/test/notes-chat-check.mjs
```

所有 API 回應均為本機 fixture，外部 requests 被阻擋；沒有操作 MongoDB、上傳錄音或呼叫付費模型。檢查使用已安裝的 Edge 及 `playwright-core`。

2026-09-30 的 production check 使用 2,700 個 transcript parts（3 小時的資料量）、20 節筆記、200 個公式、Mermaid flowchart、80 次編輯／Preview 循環，以及每 20 次循環切換 session。數字為 CDP 明確 GC 後的 JavaScript heap：

| 同一個 fixture 階段 | 加入數學顯示前 | 加入數學顯示後 |
| --- | ---: | ---: |
| 一般文字首次載入 | 6.30 MiB | 6.32 MiB |
| 引擎載入後的小 session | 8.57 MiB | 9.82 MiB |
| 長筆記初次顯示 | 10.54 MiB | 12.20 MiB |
| 第 80 次編輯後 | 11.66 MiB | 13.63 MiB |
| 切回小 session | 9.88 MiB | 11.49 MiB |
| 小 session 相對已載入引擎時的增加量 | 1.31 MiB | 1.67 MiB |

長筆記掛載元素由 744 增至 3,045，包含 200 個實際 MathML 公式；切回小 session 後沒有公式元件或 Mermaid 暫存 host 留在頁面。測試以小 session 增加量少於 5 MiB、長筆記各次 GC 後增加量少於 8 MiB 作回歸門檻。這些結果是約 27 秒的離線編輯循環，不代表整個瀏覽器 RSS 或 3 小時實際運作測試。

另外驗證行內／獨立公式、表格分數和矩陣、來源 popup、Reading 模式、Mermaid、125% zoom、375px mobile、普通 code 和 escaped currency。補充檢查涵蓋 math fence、未完成／超長公式、recursive macro、禁止公式外部內容，以及 lazy module 載入失敗時保留原文。現有 notes/chat check 亦通過。

測試輸出存於被 Git 忽略的 `app-temp/web/output/playwright/markdown-math/`：`before/results.json`、`after/results.json`、`behavior/results.json`，以及桌面、zoom、mobile 和錯誤公式 screenshots。
