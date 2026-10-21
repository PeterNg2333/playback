namespace Playback.Domain.Notes;

// Builds the next saved version from an update; shared by Mongo persistence and the in-memory test store.
public static class NoteMutation
{
    const int MaxMarkdownLength = 250_000;

    // Writes inline source lists as citation keys and checks the tree and its size, before the write lock is taken.
    public static SectionUpdate Prepare(string id, SectionUpdate update)
    {
        var citations = NoteSections.CloneCitations(update.Citations);
        var allowed = update.Sections.SelectMany(x => x.Blocks).SelectMany(x => x.SourceIds)
            .Concat(citations.SelectMany(x => x.SourceIds)).Distinct().ToList();
        var sections = update.Sections.Select(topic =>
        {
            var copy = NoteTree.Clone(topic);
            copy.Blocks = NoteTree.Compact(id, topic.Blocks, allowed, citations);
            return copy;
        }).ToList();
        if (NoteTree.Render(sections).Length > MaxMarkdownLength) throw new InvalidOperationException("Note exceeds the document size limit");
        return update with { Sections = sections, Citations = citations };
    }

    public static Note Create(string id, Note? previous, SectionUpdate update, string inputHash,
        int basedOnVersion, string language, string currentLanguage, string author, DateTime? createdAt = null)
    {
        if ((previous?.Version ?? 0) != basedOnVersion || currentLanguage != language)
            throw new InvalidOperationException("Note or language changed during this operation; the result was not applied");
        var markdown = NoteTree.Render(update.Sections);
        var citations = NoteSections.UsedCitations(update.Sections, update.Citations);
        return new Note
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = id,
            Version = basedOnVersion + 1,
            BasedOnVersion = basedOnVersion,
            Markdown = markdown,
            Author = author,
            OutputLanguage = language,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            InputHash = inputHash,
            Sections = update.Sections,
            Citations = citations,
            TranscriptIds = update.Sections.SelectMany(x => x.Blocks).SelectMany(x => x.SourceIds)
                .Concat(citations.SelectMany(x => x.SourceIds)).Distinct().ToList(),
            MaterialIds = previous?.MaterialIds.ToList() ?? [],
            Edits = NoteChangeLog.Build(previous?.Markdown ?? "", markdown, update.Citations.SelectMany(x => x.SourceIds), []),
            Segments = update.Segments ?? previous?.Segments ?? [],
            Summary = update.Summary ?? previous?.Summary ?? [],
            ReviewQuestions = update.ReviewQuestions ?? previous?.ReviewQuestions ?? []
        };
    }
}
