# Module 05 — 即時提問 + online search

## 先把「搜尋」和「回答」分開

Lecture chat 的最小可信 pipeline 是：

```text
student question
  → 從目前 note/transcript 找 lecture evidence
  → 若問題需要最新或外部資料，搜尋固定 provider
  → 保存 title + excerpt + URL
  → LLM 只根據 evidence 組織答案
  → UI 同時顯示 answer、citation、lecture timestamp
```

本 module 只 spike 中間的 online evidence boundary。Default `Demo` 讀本地 JSON，不連網；`Wikipedia` 才呼叫固定的 `en.wikipedia.org/w/api.php`。使用者只能控制 query，不能提供任意 URL，因此不會把這個 search 變成 SSRF proxy。Response 限 512 KB、12 秒，redirect 關閉；API 回來的 citation 亦必須是 `https://en.wikipedia.org`，不能只因為是 HTTPS 就信任。

```powershell
# 離線結果 + boundary self-test（fixture、host、off-domain URL、body size）
& .\scripts\05-live-chat-search\study.ps1 -SelfTest

# 檢視限制
& .\scripts\05-live-chat-search\study.ps1 -Mode Inspect

# 明確連線到 Wikipedia/MediaWiki API
& .\scripts\05-live-chat-search\study.ps1 `
    -Mode Wikipedia `
    -Query 'How does voice activity detection work?'
```

輸出叫 **evidence pack**，刻意不是 AI answer。真實搜尋失敗、沒有結果或來源互相衝突時，chat 應說「目前證據不足」，而不是讓 model 靠記憶補完。

## 如何接回 module 04

1. 從 question 抽出 search terms，但保存原問題。
2. 先搜尋 lecture-local evidence：相關 transcript chunk IDs、note revision、教材頁碼。
3. 只有 `needsFreshInformation=true` 才用 online provider，設 timeout、result cap、rate/cost limit。
4. 清理網頁正文並標成 untrusted quoted data；網頁內任何「ignore instructions」都不是 agent 指令。
5. 給 LLM 結構化 evidence IDs，而不是讓它自己發明 URL。
6. 驗證回答引用的每個 ID 都實際存在，再在 UI render；文字用 `textContent`/framework escaping，不能塞入 `innerHTML`。
7. 保存 question、answer、evidence IDs 和當時 note revision，日後才可追查答案根據甚麼。

## 為何 Wikipedia 只適合 spike？

它有公開、可重現的 MediaWiki Action API，適合學 query encoding、JSON parsing 和 citations。但大學問題通常需要 lecture material、教科書、論文或可信的一手來源。正式產品要定義 source policy、版權、語言、freshness 和 domain allowlist，不能把「能搜尋」等同「答案可靠」。

## Source

- [MediaWiki API: Search and discovery](https://www.mediawiki.org/wiki/API:Search_and_discovery/en)
- [.NET HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- [OWASP SSRF prevention cheat sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
