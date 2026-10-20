# P6：PDF／PPTX／DOCX 抽取方案

查閱日期 2026-10-20。呢份係 source-backed implementation recommendation，未安裝／執行候選 library，唔係抽取品質測試結果。保留目前 materials 純文字接口；P8 才實作 upload。

## 候選比較

| 候選／查閱版本 | 格式同定位 | 表格／數學／掃描件 | License／維護 | .NET 10／限制 |
| --- | --- | --- | --- | --- |
| PdfPig 0.1.16 | PDF；逐頁抽字，page number 從 1 開始；可保留字詞座標 | 表格閱讀順序要另做；公式可能只得字形／片段；冇文字層嘅掃描頁要 OCR | Apache-2.0；官方 NuGet 有該版本；1.0 前 API 可能變 | managed .NET package 候選；必須實測 net10、壞檔／加密／memory，唔聲稱已相容驗收 |
| DocumentFormat.OpenXml 3.5.1 | DOCX paragraphs／tables；PPTX 依 `SlideIdList` 次序保存 slide 號 | 可讀表格結構、OMML 原始數學；唔係圖片 OCR／排版 renderer | MIT；Microsoft 維護 SDK | 支援嘅 target frameworks 見官方 package；唔需要 Office；DOCX 通常冇可靠實際頁碼 |
| Apache Tika 4.0.x 文件線 | PDF、Office 等多格式；metadata／XHTML | 可配 OCR；準確 page/slide anchor 要實作 extractor-specific mapping | Apache-2.0；獨立 Java ecosystem | 需要 JVM 或獨立 process/service；增加部署／資源面，今次唔選 |

PdfPig 嘅逐頁／encrypted parsing 行為見 [官方 repo](https://github.com/UglyToad/PdfPig)，[license](https://github.com/UglyToad/PdfPig/blob/master/LICENSE) 同 [NuGet 0.1.16](https://www.nuget.org/packages/PdfPig/0.1.16)。唔代表 PDF 本身嘅加密／權限應被忽略；P8 初期統一拒絕需要密碼嘅 PDF，顯示原因。

Open XML SDK 係讀 package 結構，唔提供 Word pagination engine；應引用 paragraph ordinal／heading path，唔捏造 page number。PPTX 引用依 presentation 定義嘅 slide 次序，唔用 `slide1.xml` 檔名排序。資料見 [官方 SDK／MIT](https://github.com/dotnet/Open-XML-SDK)、[NuGet 3.5.1](https://www.nuget.org/packages/DocumentFormat.OpenXml/3.5.1)、[Presentation 文件](https://learn.microsoft.com/en-us/office/open-xml/presentation/overview)。

Tika 支援格式見 [4.0.x formats](https://tika.apache.org/docs/4.0.x/formats.html)，[license](https://github.com/apache/tika/blob/main/LICENSE.txt)。官方亦明講 parser 唔係安全邊界，要隔離資源同網絡：[security model](https://tika.apache.org/security-model.html)。下列限制係本產品建議，唔係任何 library 自帶保證。

## 給 P8 嘅實作範圍

採 PdfPig + Open XML SDK，固定以上查閱版本起步，先 restore/build 相容性檢查。只接受 PDF、PPTX、DOCX；舊 binary `.ppt/.doc`、macro-enabled 文件同需要密碼嘅檔案清楚拒絕。

新增一個 extraction adapter，回傳 `documentId, name, mediaType, sha256, extractionVersion, parts[]`。每個 part 保存 `id, kind(page|slide|paragraph), ordinal, headingPath?, text, warnings[]`。PDF／PPTX ordinal 由 1 開始；DOCX 明示 paragraph。Material source ID 綁 document hash + part ordinal + extraction version，重試同一文件唔重複建立；所有讀寫經 session owner guard。

Citation 保存 `materialId + partId + startOffset + length`，UI 顯示「檔名 · 第 N 頁／第 N 張」或「檔名 · 段落 N」，開啟保存嘅抽取文字；數學／表格轉換有損就加 warning，唔宣稱原頁還原。P8 原檔只暫存來抽取；要持久回看原頁，等 R2/object store 階段另做，未上線前 UI 要講清楚只見抽取文字。

建議初始上限：20 MiB input、500 頁／slide、50,000 ZIP entries、100 MiB 累計解壓、20 MiB 單 entry、2,000,000 抽出字元、30 秒 wall timeout、單 worker 256 MiB memory。逐 entry／逐頁計實際讀出 bytes，唔只信 ZIP metadata／compression ratio。拒絕 path traversal、external relationship fetch、DTD/entity；唔執行 macro／embedded object。限制應喺低權限、無外網 worker process 外強制，library 唔支援 cancellation 時 timeout 後停止該 worker，唔用 request cancellation 假裝已停止 CPU。

掃描 PDF 冇文字就回「需要 OCR」，唔生成空 material 當成功。OCR 候選另做小範圍驗證，今次唔加雲端 VLM 或圖片上傳。

驗收 fixtures：三格式各一份正文／表格／標題；多頁定位、PPTX slide order、DOCX paragraph anchor；壞檔、password PDF、超大輸入、zip bomb、過多 entries、timeout、空掃描頁。Browser flow 覆蓋 upload → warning/error → note/Ask citation → 正確 part；同名不同 hash 同一檔重試都要有 checks。
