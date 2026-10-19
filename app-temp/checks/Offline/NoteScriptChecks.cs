using Playback.Api.Notes;

// Traditional notes: Simplified words the model lets in become Hong Kong Traditional, including words made only of
// characters that also exist in Traditional (示范, 根据, 背后), while correct Hong Kong writing stays as it is.
// The examples are real model output from the Week 3 runs (app-temp/data/validation/runs/2026-10-18-*, first 20
// minutes of the lecture), each with its run folder, plus a few written here to pin a rule; every run sentence was
// checked by the evaluation in docs/phases/p4-results.zh-HK.md, which found no change to correct Traditional text.
static class NoteScriptChecks
{
    // Simplified in, Hong Kong Traditional out.
    static readonly (string Text, string Expected, string Source)[] Converts =
    [
        ("或者根本没有被正确归类。\n根据 `topicOutline`，确实存在其他 topics", "或者根本沒有被正確歸類。\n根據 `topicOutline`，確實存在其他 topics",
            "placement-v13b/openrouter_qwen_qwen3.8-flash_none_batch_organize/replies.json"),
        ("前面的内容（T1-T16）可能存在于其他空的或未展示的 topics 中", "前面的內容（T1-T16）可能存在於其他空的或未展示的 topics 中",
            "placement-v13b/openrouter_qwen_qwen3.8-flash_none_batch_organize/replies.json"),
        ("AI 制作幻灯片导致希望退课。", "AI 製作幻燈片導致希望退課。", "placement-v13b/openrouter_qwen_qwen3.8-flash_none_batch_organize/replies.json"),
        ("标题可微调以更精准，但原标题尚可接受。", "標題可微調以更精準，但原標題尚可接受。", "placement-v13b/openrouter_qwen_qwen3.8-flash_none_batch_organize/replies.json"),
        ("由于 `section_47eb...` (AI辅助学习) 和", "由於 `section_47eb...` (AI輔助學習) 和", "placement-v13b/openrouter_qwen_qwen3.8-flash_none_batch_organize/replies.json"),
        ("以找出正確結果及其背后的原因。", "以找出正確結果及其背後的原因。", "v11-structure/vertex_gemini-3.5-flash-lite_batch_organize/replies.json"),
        // Written here: Simplified-only characters beside correct Traditional ones, the word the 2026-10-18 comparison saw pass unconverted, and other all-shared Simplified words.
        ("讲师从预告：干擾、皇后、公里", "講師從預告：干擾、皇后、公里", "synthetic"),
        ("講者示范如何設定中斷點。", "講者示範如何設定中斷點。", "synthetic"),
        ("這是一個典范，請保持干净。", "這是一個典範，請保持乾淨。", "synthetic"),
        ("內容十分丰富，几乎沒有空檔。", "內容十分豐富，幾乎沒有空檔。", "synthetic"),
    ];

    // Correct Hong Kong Traditional: unchanged. Each run sentence contains a shared-character word OpenCC's phrase list
    // would change (了解 → 瞭解, 明了 → 明瞭, 了如 → 瞭如, 加注 → 加註, 在核 → 在覈, 公布 → 公佈, 上周 → 上週…).
    static readonly (string Text, string Source)[] Keeps =
    [
        ("承接上回課程，講者演示了如何利用除錯器（Debugger）進行本地核心除錯", "benchmark-final/vertex_gemini-3.1-flash-lite_batch_organize/note.json"),
        ("若使用者在核心除錯器（Kernel debugger）中輸入指令並尋找函數", "benchmark-final/vertex_gemini-3.1-flash-lite_batch_organize/note.json"),
        ("透過深入檢視程式碼細節，可以更了解 Intel CPU 內部程式碼如何從使用者負載環境切換", "benchmark-final/vertex_gemini-3.5-flash-lite/note.json"),
        ("來檢視符號。上周使用該檔案時會秀出兩類檔案", "benchmark-final/vertex_gemini-3.5-flash-lite/note.json"),
        ("學生就能夠真正學習到新知識。說明了尋找錯誤與自我修正的重要性。", "benchmark-final/vertex_gemini-3.5-flash-lite_batch_organize/note.json"),
        ("因此提醒大家必須多加注意，確保自己在家中有虛擬機（VM）", "benchmark-final/vertex_gemini-3.5-flash-lite_batch_organize/note.json"),
        ("今年一定能在課堂上取得良好的成績或通過作業考核", "jev-pacing/vertex_gemini-3.5-flash-lite_fixed-30s/note.json"),
        ("此區塊無需引號干擾，僅儲存有效結構。", "jev-pacing/vertex_gemini-3.5-flash-lite_jev/replies.json"),
        ("着同學先自行使用該 AI 工具，並表示稍後會回來", "note-models-baseline/openrouter_deepseek_deepseek-v4.1-flash_none/note.json"),
        ("因此除了平常的課堂公布（posting）之外，他會另外發布一些", "note-models-v10/openrouter_deepseek_deepseek-v4.1-flash_none/note.json"),
        ("講師宣布已利用 AI 工具全面重製（revamp）", "qwen-organize-v13/openrouter_qwen_qwen3.8-flash_none_batch_organize/note.json"),
        ("當講師開放學生透過麥克風提問後，台下學生並未提出直接疑問", "v11-topics/vertex_gemini-3.5-flash-lite_fixed-30s/note.json"),
        // Written here: Hong Kong usage and words OpenCC lists that protect a shared character next to them.
        ("公里、游泳、茶几、伙伴、着重、台灣、周末", "synthetic"),
        ("這方面包括控制作業，宿舍在學校後面。", "synthetic"),
    ];

    public static void Run()
    {
        var script = new OpenccNoteScript();
        foreach (var (text, expected, source) in Converts)
            Expect.That(script.Apply(text, "zh-Hant") == expected, $"Simplified text from {source} was not converted: {script.Apply(text, "zh-Hant")}");
        foreach (var (text, source) in Keeps)
            Expect.That(script.Apply(text, "zh-Hant") == text, $"Correct Traditional from {source} was changed: {script.Apply(text, "zh-Hant")}");
        Expect.That(script.Apply("讲师示范", "zh-Hans") == "讲师示范" && script.Apply("示范", "en") == "示范", "Notes in another language were converted");
        Console.WriteLine($"Note script checks passed: {Converts.Length} Simplified examples converted (phrases of shared characters included), " +
            $"{Keeps.Length} correct Hong Kong Traditional examples unchanged, other note languages untouched");
    }
}
