// One organizer call from the saved Qwen3.8 Flash run (app-temp/data/validation/runs/2026-10-18-qwen-organize-v13,
// tree-organize-v13, the third "Section organization" call): the topic it split and the reply it sent. Bodies are
// shortened; block IDs, titles and the reply's structure are as saved. Every move names an in-scope block, but none
// names a destination: the reply wrote the destination only in "reason", so all seven were dropped as out of scope.
static class SavedOrganizerReplies
{
    public const string ParentId = "section_48f0f7354fe688d5c5de", ParentTitle = "系統呼叫與 Intel CPU 機制";
    public const string TopicId = "section_8188a58fe01c713ba63e", TopicTitle = "系統呼叫鏈與 NtQueryInformationFile(?)";

    // The topic's seven blocks in lecture order (block ID, shortened body); each cites one source of its own.
    public static readonly (string Id, string Body)[] Blocks =
    [
        ("point_1ce3f3ca2589d680077b", "講師提及簡報中引用的檔案為 Query file(?)。"),
        ("point_cbf570bfe1ef67ce8f08", "講師回顧上堂課使用調試器進行本地內核調試。"),
        ("point_c7fdf71b9556791173b0", "調試器指令用來檢查並尋找包含特定字串的符號。"),
        ("point_5821ed05a6946b1e8311", "找到符號後，講師使用 uf(?) 反組譯該函數。"),
        ("point_6a295a4e41d08f6fe774", "反組譯後的程式碼引用 User Kernel Data 結構。"),
        ("point_97dd9b7e49411908801c", "透過偏移量 +308(?) 計算地址。"),
        ("point_157cb24450328a5be7d8", "檢查該地址可找到結果；範例所引用的版本可能不同。"),
    ];

    // The reply as saved (reasons shortened): two new subtopics, then seven move-block operations without topicId.
    public const string SavedReply = """
        {"outline":[{"key":"new1","title":"調試器操作與符號解析","parent":"section_8188a58fe01c713ba63e"},
        {"key":"new2","title":"用戶內核數據地址計算","parent":"section_8188a58fe01c713ba63e"}],
        "operations":[
        {"kind":"move-block","blockId":"point_cbf570bfe1ef67ce8f08","order":0,"reason":"將本地內核調試的回顧移至系統呼叫鏈主題開頭，作為背景引入。"},
        {"kind":"move-block","blockId":"point_1ce3f3ca2589d680077b","order":1,"reason":"具體的呼叫鏈範例緊接在回顧之後。"},
        {"kind":"move-block","blockId":"point_c7fdf71b9556791173b0","order":0,"reason":"移動至新主題「調試器操作與符號解析」，描述尋找符號的指令。"},
        {"kind":"move-block","blockId":"point_5821ed05a6946b1e8311","order":1,"reason":"移動至新主題「調試器操作與符號解析」，描述反組譯指令 uf。"},
        {"kind":"move-block","blockId":"point_6a295a4e41d08f6fe774","order":0,"reason":"移動至新主題「用戶內核數據地址計算」，指出 User Kernel Data 結構。"},
        {"kind":"move-block","blockId":"point_97dd9b7e49411908801c","order":1,"reason":"移動至新主題「用戶內核數據地址計算」，說明如何透過偏移量計算地址。"},
        {"kind":"move-block","blockId":"point_157cb24450328a5be7d8","order":2,"reason":"移動至新主題「用戶內核數據地址計算」，補充檢查結果及版本差異提醒。"}],
        "deferred":[]}
        """;

    // A crowded-level regroup from the first tree-organize-v14 live run (2026-10-19-p3-organize-v14b, organizer call
    // 8; titles and count shortened to fit seven topics S000–S006): new parents declared, then move-topic operations
    // with no parentId, so every topic stayed at the top level and the regroup changed nothing. All seven regroups
    // in that run had this shape; two more failed on a rename, which an outline regroup used to refuse.
    public const string SavedRegroupReply = """
        {"outline":[{"key":"new1","title":"AI 輔助教學與簡報製作","parent":""},{"key":"new2","title":"Kernel 調試與地址查詢","parent":""}],
        "moves":[],
        "operations":[{"kind":"move-topic","topicId":"S000","blockId":""},{"kind":"move-topic","topicId":"S001","blockId":""},
        {"kind":"move-topic","topicId":"S002","blockId":""},{"kind":"move-topic","topicId":"S003","blockId":""},
        {"kind":"move-topic","topicId":"S004","blockId":""},{"kind":"move-topic","topicId":"S005","blockId":""},
        {"kind":"move-topic","topicId":"S006","blockId":""}],
        "deferred":[]}
        """;

    // The same regroup in the contract after that run: topics move as placements naming their new parent, and a
    // rename with its title applies in an outline regroup. Written from the saved reply, not a provider reply.
    public const string PlacedRegroupReply = """
        {"outline":[{"key":"new1","title":"AI 輔助教學與簡報製作","parent":""},{"key":"new2","title":"Kernel 調試與地址查詢","parent":""}],
        "moves":[],
        "topicMoves":[{"topic":"S000","to":"new1"},{"topic":"S001","to":"new1"},{"topic":"S002","to":"new1"},
        {"topic":"S003","to":"new2"},{"topic":"S004","to":"new2"},{"topic":"S005","to":"new2"},{"topic":"S006","to":"new2"}],
        "operations":[{"kind":"rename-topic","topicId":"S006","blockId":"","title":"使用者共享資料"}],
        "deferred":[]}
        """;

    // The same decisions in the tree-organize-v14 contract: each moved block names the topic it goes to; the two
    // blocks the reply kept in place are not listed. Written from the saved reasons, not a provider reply.
    public const string PlacedReply = """
        {"outline":[{"key":"new1","title":"調試器操作與符號解析","parent":"section_8188a58fe01c713ba63e"},
        {"key":"new2","title":"用戶內核數據地址計算","parent":"section_8188a58fe01c713ba63e"}],
        "moves":[{"block":"point_c7fdf71b9556791173b0","to":"new1"},{"block":"point_5821ed05a6946b1e8311","to":"new1"},
        {"block":"point_6a295a4e41d08f6fe774","to":"new2"},{"block":"point_97dd9b7e49411908801c","to":"new2"},
        {"block":"point_157cb24450328a5be7d8","to":"new2"}],
        "operations":[],"deferred":[]}
        """;
}
