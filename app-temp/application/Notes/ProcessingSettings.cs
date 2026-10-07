using Playback.Domain.Notes;

namespace Playback.Application.Notes;

public sealed class ProcessingSettingsRecord
{
    public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = ProcessingPolicy.SchemaVersion;
    public int Revision { get; set; }
    public ProcessingOverrides Overrides { get; set; } = new();
}

public interface IProcessingSettingsRepository
{
    Task<ProcessingSettingsRecord?> Read(string key);
    // The adapter must atomically compare the revision, including concurrent first writes.
    Task Save(ProcessingSettingsRecord value, int expectedRevision);
}
public interface INoteProcessingPolicy
{
    Task<EffectiveProcessingPolicy> Effective(string sessionId);
}

public sealed record ProcessingSettingsView(int SchemaVersion, int Revision, int InheritedRevision,
    string EffectiveRevision, ProcessingOverrides Overrides, ProcessingPolicy Effective, ProcessingPolicy Inherited);
public sealed record ProcessingSettingsUpdate(int ExpectedRevision, ProcessingOverrides Overrides, int ExpectedInheritedRevision = 0);

public sealed class ProcessingSettings(IProcessingSettingsRepository repository, ProcessingPolicy defaults)
{
    public async Task<ProcessingSettingsView> Workspace(string owner)
    {
        defaults.Validate();
        var saved = await Read("workspace:" + owner);
        return new(saved.SchemaVersion, saved.Revision, 0, $"{saved.Revision}:0", saved.Overrides, defaults.Apply(saved.Overrides), defaults);
    }
    public async Task<ProcessingSettingsView> Session(string owner, string id)
    {
        var workspace = await Workspace(owner);
        var saved = await Read("session:" + id);
        return new(saved.SchemaVersion, saved.Revision, workspace.Revision, $"{workspace.Revision}:{saved.Revision}",
            saved.Overrides, workspace.Effective.Apply(saved.Overrides), workspace.Effective);
    }
    public async Task<ProcessingSettingsView> SaveWorkspace(string owner, ProcessingSettingsUpdate input)
    {
        await Save("workspace:" + owner, input, defaults);
        return await Workspace(owner);
    }
    public async Task<ProcessingSettingsView> SaveSession(string owner, string id, ProcessingSettingsUpdate input)
    {
        var workspace = await Workspace(owner);
        if (input.ExpectedInheritedRevision != workspace.Revision)
            throw new InvalidOperationException("Workspace settings changed; reload before saving session overrides");
        await Save("session:" + id, input, workspace.Effective);
        return await Session(owner, id);
    }
    async Task Save(string key, ProcessingSettingsUpdate input, ProcessingPolicy inherited)
    {
        if (input.ExpectedRevision < 0 || input.ExpectedRevision == int.MaxValue || input.Overrides is null)
            throw new ProcessingPolicyValidationException("Invalid settings revision or overrides");
        inherited.Apply(input.Overrides);
        var current = await Read(key);
        if (current.Revision != input.ExpectedRevision) throw new InvalidOperationException("Processing settings changed; reload before saving");
        if (current.Overrides == input.Overrides) return;
        await repository.Save(new() { Id = key, Revision = input.ExpectedRevision + 1, Overrides = input.Overrides }, input.ExpectedRevision);
    }
    async Task<ProcessingSettingsRecord> Read(string key)
    {
        var result = await repository.Read(key) ?? new() { Id = key };
        if (result.SchemaVersion != ProcessingPolicy.SchemaVersion)
            throw new InvalidOperationException("Unsupported processing settings version");
        return result;
    }
}
