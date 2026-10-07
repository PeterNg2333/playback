namespace Playback.Domain.Notes;

// Mutations have one home, shared by Mongo persistence and in-memory acceptance adapters.
public static class NoteMutation
{
    public static SectionUpdate Prepare(string id, SectionUpdate update)
    {
        update = update with { Citations = NoteSections.CloneCitations(update.Citations) };
        var allowed = update.Sections.SelectMany(s => s.Points).SelectMany(p => p.SourceIds)
            .Concat(update.Citations.SelectMany(c => c.SourceIds))
            .Distinct()
            .ToList();
        update = update with
        {
            Sections = update.Sections.Select(section => new NoteSection
            {
                Id = section.Id,
                Version = section.Version,
                Title = section.Title,
                UserEdited = section.UserEdited,
                OrganizedAt = section.OrganizedAt,
                OrganizedVersion = section.OrganizedVersion,
                Markdown = NoteSections.Compact(id, section.Markdown, allowed, update.Citations),
                Points = section.Points.Select(point => new NotePoint
                {
                    Id = point.Id,
                    SourceIds = point.SourceIds.ToList(),
                    Text = NoteSections.Compact(id, point.Text, allowed, update.Citations)
                }).ToList()
            }).ToList()
        };
        var markdown = NoteSections.Render(update.Sections);
        if (markdown.Length > 250_000) throw new InvalidOperationException("Note exceeds the document size limit");
        return update;
    }

    public static Note Create(string id, Note? previous, SectionUpdate update, string inputHash,
        int basedOnVersion, string language, string currentLanguage, string author,
        List<string>? deleted = null, List<string>? suppressed = null, DateTime? createdAt = null)
    {
        if ((previous?.Version ?? 0) != basedOnVersion || currentLanguage != language)
            throw new InvalidOperationException("Note or language changed during this operation; the result was not applied");
        var allowed = update.Sections.SelectMany(s => s.Points).SelectMany(p => p.SourceIds)
            .Concat(update.Citations.SelectMany(c => c.SourceIds)).Distinct().ToList();
        var markdown = NoteSections.Render(update.Sections);
        if (author.StartsWith("agent")) NoteSections.RequireRetention(previous, update, allowed, allowProtectedReformat: author == "agent organization");
        var coverage = (previous?.Coverage ?? []).Where(x => !update.Coverage.Any(y => y.SourceId == x.SourceId)).Concat(update.Coverage)
            .Select(x => new SourceDisposition { SourceId = x.SourceId, Status = x.Status, Reason = x.Reason,
                PointIds = x.PointIds.ToList(), ContentHash = x.ContentHash }).ToList();
        foreach (var item in coverage.Where(x => x.Status == "covered"))
        {
            item.PointIds = update.Sections.SelectMany(x => x.Points).Where(x => x.SourceIds.Contains(item.SourceId)).Select(x => x.Id).ToList();
            if (item.PointIds.Count == 0)
            {
                item.Status = "suppressed";
                item.Reason = "Written point removed by an explicit user edit/restore; do not regenerate automatically";
            }
        }
        var usedCitations = NoteSections.UsedCitations(update.Sections, update.Citations);
        var note = new Note
        {
            Id = Guid.NewGuid().ToString("N"),
            SessionId = id,
            Version = basedOnVersion + 1,
            BasedOnVersion = basedOnVersion,
            Markdown = markdown,
            Author = author,
            OutputLanguage = language,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Sections = update.Sections,
            Citations = usedCitations,
            InputHash = inputHash,
            Coverage = coverage,
            DeletedSectionIds = (deleted ?? previous?.DeletedSectionIds ?? []).Distinct().ToList(),
            SuppressedSourceIds = (suppressed ?? previous?.SuppressedSourceIds ?? []).Distinct().ToList(),
            TranscriptIds = update.Sections.SelectMany(x => x.Points).SelectMany(x => x.SourceIds)
                .Concat(usedCitations.SelectMany(x => x.SourceIds)).Distinct().ToList(),
            MaterialIds = previous?.MaterialIds.ToList() ?? [],
            InputTranscriptIds = update.Coverage.Select(x => x.SourceId).ToList(),
            Edits = NoteChangeLog.Build(previous?.Markdown ?? "", markdown, update.Citations.SelectMany(x => x.SourceIds), []),
            SourceFrom = previous?.SourceFrom,
            SourceThrough = previous?.SourceThrough,
            ProcessedThroughMs = previous?.ProcessedThroughMs ?? 0
        };
        return note;
    }
}
