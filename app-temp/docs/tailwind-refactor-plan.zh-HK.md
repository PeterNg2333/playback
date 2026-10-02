# Tailwind CSS 遷移計劃（2026-10-01）

目標：`app-temp/web` 由 8 個手寫 CSS 檔（約 2,540 行、約 550 個規則區塊）改為 Tailwind CSS utilities，打開一個 component 就看到它的樣子，不必到另一個 CSS 檔找 class。版本以 2026-10-01 的 [Tailwind 官方 Vite 安裝文件](https://tailwindcss.com/docs/installation/using-vite) 為準：Tailwind v4.3，`tailwindcss` 與 `@tailwindcss/vite`。現有 Vite 6.4.3。

先做 [frontend review](frontend-review.zh-HK.md) 的結構搬移，再按下列階段遷移樣式。

> 狀態：已於 branch `refactor/frontend-readability` 完成（2026-10-02）。8 個舊 CSS 檔共 2,980 行已刪；`src/styles/` 只剩 `app.css`（Tailwind、theme、頁面 globals，83 行）與 `markdown.css`（173 行）。下面「需要你決定」兩項已按建議做：裝了三個套件；Markdown 用自己的 `markdown.css`。與計劃不同之處：
>
> - Breakpoint 統一到 Tailwind 預設（`md` 768、`lg` 1024、`xl` 1280），另定 `xs` = 24.4375rem（391px），讓 390px 及以下的手機保留緊湊版面。舊值與新值之間（701–767、901–1023、1101–1279px）的版面因此改變；1440、1024、390 三個寬度不變。
> - 共用 component：`Button`、`IconButton`、`Segmented`、`Chip`、`Tag`、`CitationChip`、`EmptyState`、`Dialog`、`PopupHeader`、`Menu`。只在一個檔內重複的外觀用具名 class 字串。
> - Preflight 拿走、而畫面依賴的瀏覽器預設（清單符號、monospace、連結底線、原生下拉選單）以 `revert` 取回。
> - 驗證：每一步在 1440／1024／390 截 27 個場景共 81 張圖，與遷移前逐像素比較，並逐個元素比較 computed style。最後 81 張與遷移前完全相同。`test:browser`（`layout-live-api`）需要本機 API 與 MongoDB，沒有跑。

## 需要你決定

1. **安裝套件。** `npm install -D tailwindcss @tailwindcss/vite clsx` 需要連網。依 `AGENTS.md`，要你明確同意才執行。`clsx` 用來組合條件 class（例如 `clsx("row", failed && "bg-pink-soft")`），取代現時的字串拼接；不想加的話，可用 template string。
2. **Markdown 內容的樣式。** 建議保留一個自己的 `markdown.css`，不用 `@tailwindcss/typography`：筆記內容有引用、term、Mermaid、KaTeX 等專用規則，typography plugin 的 `prose` 會與它們互相覆蓋。

## 為甚麼結構先行

Tailwind 遷移會改動每一行 JSX 的 `className`。如果先把檔案搬到 `features/*` 並改好名，Git 能把搬移識別為 rename，樣式的 diff 亦按 feature 分開，每一步都能單獨 review 和還原。反過來做，同一行會被改兩次，`git log --follow` 也會失效。

## 階段

### 0. 先拆走依賴 class 名的地方

不做這一步，Tailwind 會同時打破測試和執行時行為。

- 測試：`web/test/*.mjs` 以約 60 個 class 選擇器找元素（`.note-content` 27 次、`.answer` 17 次、`.flowchart` 17 次……）。改用 Playwright 的 `getByRole`／`getByLabel`；沒有可用名稱的元素才加 `data-testid`。
- 執行時：14 處程式碼以 class 找元素，例如 `captureSelection` 找 `.transcript-row`、`VirtualTranscript` 找 `.transcript-content`、`NotesPanel` 找 `.note-content h1`、`.markdown-preview > *`，以及 `classList.add("source-revealed")`。改用 ref 或 `data-*` 屬性（見 review finding「以 window 事件和 CSS class 溝通」）。

完成條件：`grep` 在 `src/` 與 `test/` 找不到以 class 名做選擇器的程式碼；所有現有檢查通過。

### 1. 安裝並搬入 design tokens（需要階段 0 完成及你同意安裝）

`vite.config.ts` 加入 plugin：

```ts
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  ...
});
```

新增 `src/styles/app.css`，取代 `app.tsx` 現在的 7 個 import：

```css
@import "tailwindcss";

/* 遷移期間：舊 CSS 放入 components layer，utilities 才能覆蓋它 */
@import "./legacy/base.css" layer(components);
@import "./legacy/layout.css" layer(components);
/* …其餘舊檔同樣處理，遷移完成後逐一刪除 */

@theme {
  --color-canvas: #f8f9fc;
  --color-surface: #fff;
  --color-ink: #24263b;
  --color-muted: #697185;
  --color-line: #e5e7ef;
  --color-accent: #6059bc;
  --color-accent-soft: #f0effa;
  --color-pink: #c475a5;
  --color-pink-soft: #fbf0f6;
  --color-blue: #769bd1;
  --font-sans: Inter, "Segoe UI", system-ui, sans-serif;
  --breakpoint-xs: 390px;
}
```

兩個要注意的地方：

- **Cascade layer。** Tailwind v4 把 utilities 放在 CSS cascade layer 內，而沒有 layer 的普通 CSS 一律勝過任何 layer 內的規則。舊 CSS 若直接 import，utilities 會被蓋過；所以遷移期間要用 `layer(components)` import（[Preflight 文件](https://tailwindcss.com/docs/preflight) 示範了同一個 `@import … layer(…)` 寫法）。
- **Preflight。** Tailwind 會清除 h1–h6 的字級、`ol`／`ul` 的項目符號和所有預設 margin。筆記內容（`react-markdown` 輸出的標題、清單、表格）和 `note-outline`、版本變更清單會因此變樣，所以先把這些規則搬到 `markdown.css` 的 `@layer components`。

現有 breakpoint 的對應：`max-width: 700px` ×9、`900px` ×3、`1100px` ×4、`390px` ×3。Tailwind 預設 `md` = 768px、`lg` = 1024px，與現值不同。可以在 `@theme` 定義 `--breakpoint-*` 保留原值，或在遷移時順手統一到預設值，並在視覺比較時確認；建議後者，少記一組數字。注意 Tailwind 是 mobile-first（`min-width`），舊 CSS 是 `max-width`，條件要反轉來寫。

完成條件：`npm run build` 通過；畫面與遷移前一致（見「驗證」）。

### 2. 先做共用的 component，不用 `@apply`

重複出現的外觀（`.icon-control`、`.text-control`、`.segmented`、`.citation`、`.term-tag`、`.empty`）做成 React component，例如 `components/Button.tsx` 以 `variant` prop 決定樣式。樣式只有一個家，而且是讀者看得到的 TSX。`@apply` 會把樣式藏回 CSS，與遷移的目的相反。

同時建立 review 提到的 `components/Menu.tsx`，取代 8 處自寫的「點外面／Esc 關閉」，一次過決定 popover 的外觀。

### 3. 逐個 feature 遷移

由小到大，每個 feature 一個 commit：`components/layout` → `library`（側欄）→ `player` → `recording` → `ask` → `activity` → `terms` → `transcript` → `notes`。

每個 feature：

1. 把該 feature 用到的舊規則改寫成 JSX 上的 utilities；狀態用屬性 variant，例如 `data-[state=recording]:bg-pink-soft`、`aria-pressed:bg-accent-soft`、`open:`。
2. 從 `legacy/*.css` 刪除已遷移的規則。一條規則若仍被其他 feature 使用，留到最後。
3. 跑驗證。

保留為 CSS（不改 utilities）：

- `markdown.css`：`react-markdown` 生成的元素，不是你寫的 JSX。
- KaTeX、Mermaid 的樣式（現 `math.css`、`.flowchart`）。
- `prefers-reduced-motion`（4 處）可以用 Tailwind 的 `motion-reduce:`；`hover: none` 用 `@custom-variant` 或保留一小段 CSS。

### 4. 收尾

- 刪除 `styles/legacy/`；`styles/` 只剩 `app.css` 與 `markdown.css`。
- 刪除 `transcript.css` 中的 `.timeline-day`／`.timeline-hour` 死規則（沒有元素使用）。
- 在 `AGENTS.md` 的 technical direction 加一句「Frontend styling: Tailwind CSS v4 utilities; Markdown content in `markdown.css`」，讓之後的 agent 和 `/human-review` 都遵守。
- 更新 `app-temp/STRUCTURE.md` 的 styles 段落。

## 驗證

每個 feature 遷移後：

- `npm --prefix app-temp/web run build`（`tsc -b` 加 Vite build）。
- 離線 UI 檢查：`npm run test:web-ui`、`npm run test:browser`、`npm run test:notes-ui`、`npm run test:loading-ui`，以及 `node app-temp/web/test/library-sidebar.check.mjs`。這些攔截 API，不連網、不開咪。
- 視覺比較：遷移前先以 Playwright 在 1440、1024、390 寬度截圖（側欄展開／收合、notes Preview／Edit、transcript 有 live row 和靜音區、Ask 面板開啟、播放列）；每個 feature 完成後同條件再截一次並逐張比對。差異要麼是刻意的（例如統一 breakpoint），要麼要修正。

完成標準：所有檢查通過；截圖差異都已解釋；`src/styles/` 只剩兩個檔；在刪除舊 CSS 前先抽出它的 class 名清單，逐一 grep，`src/` 內不再出現（`markdown.css` 用到的除外）。

## 風險

- 遷移期間舊 CSS 與 utilities 並存，若忘記 `layer(components)`，utilities 會靜悄悄失效。階段 1 的第一個 utility 改動就要確認它真的生效。
- Preflight 的 reset 影響範圍最大的是筆記內容；階段 1 完成後先檢查 notes Preview 截圖。
- 現有 `TranscriptContent` 和 `VirtualTranscript` 依靠量度行高做虛擬捲動；改 padding 或字級會改變行高，三小時 fixture（約 1,266 行）的捲動與「跳到來源」要重測。
