using Playback.Api;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playback.Api.Db;
using Playback.Api.Sources;
using Playback.Api.Notes;

// Read-only: from a saved session snapshot, previews which sections of note v112 a recovery would bring back.
// Writes the preview next to the snapshot; never writes to a database.
static class NotesRecoveryPreview
{
    public static void Run(string folder) {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        string Read(string name) => File.ReadAllText(Path.Combine(folder, name)).TrimStart('\uFEFF');
        var session = JsonSerializer.Deserialize<SessionView>(Read("session.json"), options)!;
        var notes = JsonSerializer.Deserialize<List<Note>>(Read("notes.json"), options)!;
        var historical = notes.Single(x => x.Version == 112);
        var preview = NoteSections.Recover(historical, session.CurrentNote, MaterialSources.Allowed(session));
        var directory = Path.Combine(folder, "recovery-preview"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "preview.json"), JsonSerializer.Serialize(preview, options));
        var sections = preview.CurrentSections.Concat(preview.Candidates.Where(x => !x.Blocked).Select(x => new NoteSection {
            Id = x.Id, Title = x.Title, Markdown = x.Markdown, Points = x.Points, UserEdited = true })).ToList();
        File.WriteAllText(Path.Combine(directory, "merged-preview.md"), NoteSections.Render(sections));
        var sourceIndex = session.Transcripts.ToDictionary(x => x.Id);
        var citationIndex = preview.Citations.ToDictionary(x => x.Id, x => x.SourceIds);
        var provenance = new List<object>();
        var readable = Regex.Replace(NoteSections.Render(sections), @"(?<code>```[\s\S]*?```|~~~[\s\S]*?~~~|`[^`\r\n]*`)|\[(?<body>[^\[\]\r\n]{1,16000})\](?!\()", match => {
            if (match.Groups["code"].Success) return match.Value;
            var body = match.Groups["body"].Value;
            var ids = citationIndex.TryGetValue(body, out var expanded) ? expanded : Regex.Matches(body,
                @"\b[a-f0-9]{32}(?:[-_][A-Za-z0-9_-]+)?\b").Select(x => x.Value).Distinct().ToList();
            if (ids.Count == 0) return match.Value;
            var number = provenance.Count + 1;
            var known = ids.Where(sourceIndex.ContainsKey).ToList();
            provenance.Add(new { label = "S" + number.ToString("D3"), sourceIds = ids, unavailableIds = ids.Except(known),
                parts = known.Select(id => new { id, sourceIndex[id].SourceId, sourceIndex[id].StartMs, sourceIndex[id].EndMs,
                    sourceIndex[id].RecordedAt, original = sourceIndex[id].SourceText }) });
            return "[來源 S" + number.ToString("D3") + (known.Count != ids.Count ? "；含不可用引用" : "") + "]";
        });
        File.WriteAllText(Path.Combine(directory, "readable-preview.md"), "# 歷史合併閱讀 preview（未套用）\n\n保留 v118 正文，附加 v112 五個歷史候選 section；歷史措辭並非本輪模型生成，也未逐句完成品質驗收。部分舊引用有錯誤 ID，仍列為不可用；沒有猜測修正。短來源標記的完整 ID／原 ASR 見 citation-provenance.json。來源登記不等於意思已獲驗證。\n\n" + readable);
        File.WriteAllText(Path.Combine(directory, "citation-provenance.json"), JsonSerializer.Serialize(provenance, options));
        var audit = notes.Select(note => new { note.Version, note.BasedOnVersion, note.Author,
            hash = ContentHash.Of(note.Markdown), registeredSources = note.TranscriptIds.Count,
            actuallyCitedSources = NoteSections.Sources(note.Markdown, session.Transcripts.Select(x => x.Id)).Count,
            sections = NoteSections.Read(note, MaterialSources.Allowed(session)).Select(section => new { section.Title,
                points = section.Points.Count, citedSources = section.Points.SelectMany(p => p.SourceIds).Distinct().Count() }) });
        File.WriteAllText(Path.Combine(directory, "history-coverage.json"), JsonSerializer.Serialize(audit, options));
        Console.WriteLine($"Read-only snapshot preview: v{historical.Version} -> v{preview.BasedOnVersion}; {preview.Candidates.Count} candidates, {sections.Count} merged sections. No database writes. Historical window v{notes.Min(x => x.Version)}-v{notes.Max(x => x.Version)}; earlier versions not available in this snapshot.");
        Console.WriteLine(directory);
    }
}
