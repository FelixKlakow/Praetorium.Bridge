using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Praetorium.Bridge.Configuration;

namespace Praetorium.Bridge.Web.Services.ConfigAgent;

/// <summary>
/// Holds a per-session staged copy of the bridge configuration and prompt files.
/// The agent mutates this; diffs are computed against the baseline captured at session start.
/// </summary>
public sealed class ConfigStaging
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private BridgeConfiguration _baseline;
    private readonly Dictionary<string, string> _promptBaselines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _stagedPrompts = new(StringComparer.OrdinalIgnoreCase);

    public ConfigStaging(BridgeConfiguration live, IReadOnlyDictionary<string, string> livePrompts)
    {
        _baseline = DeepClone(live);
        Staged = DeepClone(live);
        foreach (var (path, content) in livePrompts)
            _promptBaselines[Normalize(path)] = content;
    }

    /// <summary>
    /// Resets the baseline + staged copy to the given live state while keeping this instance alive.
    /// Used after Apply so the agent tools (which hold a reference to this instance) keep working
    /// against a fresh, empty-diff staging.
    /// </summary>
    public void ReBaseline(BridgeConfiguration live, IReadOnlyDictionary<string, string> livePrompts)
    {
        _baseline = DeepClone(live);
        Staged = DeepClone(live);
        _promptBaselines.Clear();
        foreach (var (path, content) in livePrompts)
            _promptBaselines[Normalize(path)] = content;
        _stagedPrompts.Clear();
    }

    /// <summary>Mutable staged configuration the tools operate on.</summary>
    public BridgeConfiguration Staged { get; private set; }

    /// <summary>Returns the staged prompt content if modified, otherwise the baseline, otherwise null.</summary>
    public string? GetPromptContent(string relativePath)
    {
        var key = Normalize(relativePath);
        if (_stagedPrompts.TryGetValue(key, out var staged))
            return staged;
        return _promptBaselines.TryGetValue(key, out var baseline) ? baseline : null;
    }

    public IReadOnlyCollection<string> ListAllPromptPaths()
    {
        var all = new HashSet<string>(_promptBaselines.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in _stagedPrompts)
        {
            if (v == null) all.Remove(k);
            else all.Add(k);
        }
        return all.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void StagePromptWrite(string relativePath, string content)
    {
        var key = Normalize(relativePath);
        _stagedPrompts[key] = content ?? string.Empty;
    }

    public void StagePromptDelete(string relativePath)
    {
        var key = Normalize(relativePath);
        if (_promptBaselines.ContainsKey(key))
            _stagedPrompts[key] = null;
        else
            _stagedPrompts.Remove(key);
    }

    public IReadOnlyDictionary<string, string?> StagedPromptMutations => _stagedPrompts;

    public ConfigChangeSet ComputeDiff()
    {
        var config = new List<ConfigChange>();

        DiffDictionary(
            "tools",
            _baseline.Tools,
            Staged.Tools,
            config);

        DiffDictionary(
            "agentToolSources",
            _baseline.AgentToolSources,
            Staged.AgentToolSources,
            config);

        DiffObject("defaults", _baseline.Defaults, Staged.Defaults, config);
        DiffObject("server", _baseline.Server, Staged.Server, config);
        DiffObject("configAgent", _baseline.ConfigAgent, Staged.ConfigAgent, config);

        var prompts = new List<PromptChange>();
        foreach (var (key, stagedContent) in _stagedPrompts)
        {
            _promptBaselines.TryGetValue(key, out var baseline);
            if (stagedContent == null)
            {
                if (baseline != null)
                    prompts.Add(new PromptChange(key, ChangeKind.Removed, baseline, null));
                continue;
            }

            if (baseline == null)
                prompts.Add(new PromptChange(key, ChangeKind.Added, null, stagedContent));
            else if (!string.Equals(baseline, stagedContent, StringComparison.Ordinal))
                prompts.Add(new PromptChange(key, ChangeKind.Modified, baseline, stagedContent));
        }

        return new ConfigChangeSet
        {
            ConfigChanges = config,
            PromptChanges = prompts.OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    public void DiscardAll()
    {
        Staged = DeepClone(_baseline);
        _stagedPrompts.Clear();
    }

    private static void DiffDictionary<TValue>(
        string section,
        Dictionary<string, TValue> baseline,
        Dictionary<string, TValue> staged,
        List<ConfigChange> changes)
        where TValue : class
    {
        var all = new HashSet<string>(baseline.Keys, StringComparer.Ordinal);
        foreach (var k in staged.Keys) all.Add(k);

        foreach (var key in all.OrderBy(k => k, StringComparer.Ordinal))
        {
            var hasBaseline = baseline.TryGetValue(key, out var baseVal);
            var hasStaged = staged.TryGetValue(key, out var stageVal);

            var beforeJson = hasBaseline ? JsonSerializer.Serialize(baseVal, PrettyJson) : null;
            var afterJson = hasStaged ? JsonSerializer.Serialize(stageVal, PrettyJson) : null;

            if (!hasBaseline && hasStaged)
                changes.Add(new ConfigChange(section, key, ChangeKind.Added, null, afterJson));
            else if (hasBaseline && !hasStaged)
                changes.Add(new ConfigChange(section, key, ChangeKind.Removed, beforeJson, null));
            else if (!string.Equals(beforeJson, afterJson, StringComparison.Ordinal))
                changes.Add(new ConfigChange(section, key, ChangeKind.Modified, beforeJson, afterJson));
        }
    }

    private static void DiffObject<T>(string section, T? baseline, T? staged, List<ConfigChange> changes)
        where T : class
    {
        var beforeJson = baseline == null ? null : JsonSerializer.Serialize(baseline, PrettyJson);
        var afterJson = staged == null ? null : JsonSerializer.Serialize(staged, PrettyJson);

        if (string.Equals(beforeJson, afterJson, StringComparison.Ordinal))
            return;

        var kind = (baseline, staged) switch
        {
            (null, not null) => ChangeKind.Added,
            (not null, null) => ChangeKind.Removed,
            _ => ChangeKind.Modified,
        };
        changes.Add(new ConfigChange(section, string.Empty, kind, beforeJson, afterJson));
    }

    internal static BridgeConfiguration DeepClone(BridgeConfiguration source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<BridgeConfiguration>(json) ?? new BridgeConfiguration();
    }

    private static string Normalize(string path)
    {
        var n = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (n.StartsWith("./", StringComparison.Ordinal)) n = n.Substring(2);
        if (n.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase)) n = n.Substring("prompts/".Length);
        return n;
    }
}
