using MongoDB.Driver;

namespace Playback.Infrastructure.Storage;

// Word templates for client-ready minutes, one owner's each. The browser reads a template's outline when it is added and
// builds the exported .docx from its bytes.
public sealed class ExportTemplateRecord
{
    public string Id { get; set; } = "";
    public string? OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string Outline { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public int Size { get; set; }
    public DateTime CreatedAt { get; set; }
}

public partial class PlaybackStore
{
    const int MaxTemplateBytes = 5_000_000;
    const int MaxTemplateOutlineChars = 100_000;

    FilterDefinition<ExportTemplateRecord> TemplateOwner() => CurrentOwner is { } owner
        ? Builders<ExportTemplateRecord>.Filter.Eq(x => x.OwnerId, owner)
        : FilterDefinition<ExportTemplateRecord>.Empty;

    public async Task<List<object>> ExportTemplates()
    {
        var items = await Collection<ExportTemplateRecord>("export_templates").Find(TemplateOwner())
            .Project<ExportTemplateRecord>(Builders<ExportTemplateRecord>.Projection.Exclude(x => x.Content).Exclude(x => x.Outline))
            .SortByDescending(x => x.CreatedAt).ToListAsync();
        return items.Select(x => (object)new { x.Id, x.Name, x.Size, x.CreatedAt }).ToList();
    }

    public async Task<object> AddExportTemplate(string name, string contentBase64, string outline)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 120) throw new InvalidOperationException("Template name must be 1–120 characters");
        byte[] content;
        try { content = Convert.FromBase64String(contentBase64); }
        catch (FormatException) { throw new InvalidOperationException("Template file could not be read"); }
        if (content.Length is < 4 or > MaxTemplateBytes) throw new InvalidOperationException("Template must be a .docx file of up to 5 MB");
        // A .docx is a zip archive, which starts with "PK".
        if (content[0] != 0x50 || content[1] != 0x4B) throw new InvalidOperationException("Template must be a .docx file");
        if (string.IsNullOrWhiteSpace(outline) || outline.Length > MaxTemplateOutlineChars)
            throw new InvalidOperationException("Template has no readable content");
        var item = new ExportTemplateRecord
        {
            Id = Guid.NewGuid().ToString("N"), OwnerId = CurrentOwner, Name = name, Outline = outline, Content = content,
            Size = content.Length, CreatedAt = DateTime.UtcNow
        };
        await Collection<ExportTemplateRecord>("export_templates").InsertOneAsync(item);
        return new { item.Id, item.Name, item.Size, item.CreatedAt };
    }

    public async Task<ExportTemplateRecord?> ExportTemplate(string id) =>
        await Collection<ExportTemplateRecord>("export_templates")
            .Find(TemplateOwner() & Builders<ExportTemplateRecord>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync();

    public async Task DeleteExportTemplate(string id)
    {
        var result = await Collection<ExportTemplateRecord>("export_templates")
            .DeleteOneAsync(TemplateOwner() & Builders<ExportTemplateRecord>.Filter.Eq(x => x.Id, id));
        if (result.DeletedCount == 0) throw new InvalidOperationException("Template not found");
    }
}
