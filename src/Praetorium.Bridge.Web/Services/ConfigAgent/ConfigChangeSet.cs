using System.Collections.Generic;

namespace Praetorium.Bridge.Web.Services.ConfigAgent;

public enum ChangeKind { Added, Modified, Removed }

public sealed record ConfigChange(
    string Section,
    string Key,
    ChangeKind Kind,
    string? BeforeJson,
    string? AfterJson);

public sealed record PromptChange(
    string RelativePath,
    ChangeKind Kind,
    string? BeforeContent,
    string? AfterContent);

public sealed class ConfigChangeSet
{
    public required IReadOnlyList<ConfigChange> ConfigChanges { get; init; }
    public required IReadOnlyList<PromptChange> PromptChanges { get; init; }
    public bool IsEmpty => ConfigChanges.Count == 0 && PromptChanges.Count == 0;
}
