namespace Playback.Domain.Workspace;
public static class WorkspaceRules
{
    public static string Title(string value) => Bounded(value, 120, "Title");
    public static string GroupName(string value) => Bounded(value, 80, "Group name");
    static string Bounded(string value, int maximum, string label)
    {
        var text = value.Trim(); if (text.Length < 1 || text.Length > maximum) throw new InvalidOperationException($"{label} must be 1–{maximum} characters"); return text;
    }
    public static void Material(string name, string text)
    {
        if (name.Length is < 1 or > 120 || text.Length is < 1 or > 250000) throw new InvalidOperationException("Material size or name is invalid");
    }
}
