using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Praetorium.Bridge.Configuration;

namespace Praetorium.Bridge.Web.Services.ConfigAgent;

/// <summary>
/// Tool surface exposed to the Config Agent. Every method mutates the staged copy
/// of the configuration (or prompt files); nothing hits disk until the user clicks Apply.
/// </summary>
public sealed class ConfigAgentTools
{
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConfigStaging _staging;

    public ConfigAgentTools(ConfigStaging staging)
    {
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
    }

    [Description("Returns a high-level summary of the staged configuration: server, defaults, tool names, agent tool source names, and configAgent section. Use this first to orient yourself.")]
    public string get_config_summary()
    {
        var s = _staging.Staged;
        var summary = new
        {
            server = s.Server,
            defaults = s.Defaults,
            configAgent = s.ConfigAgent,
            toolNames = s.Tools.Keys.OrderBy(k => k).ToArray(),
            agentToolSourceNames = s.AgentToolSources.Keys.OrderBy(k => k).ToArray(),
        };
        return JsonSerializer.Serialize(summary, PrettyJson);
    }

    [Description("Lists all tools in the staged configuration with their descriptions.")]
    public string list_tools()
    {
        var items = _staging.Staged.Tools
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new { name = kv.Key, description = kv.Value.Description })
            .ToArray();
        return JsonSerializer.Serialize(items, PrettyJson);
    }

    [Description("Returns the full JSON definition of a single tool from the staged configuration.")]
    public string get_tool(
        [Description("Exact tool name.")] string toolName)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var def))
            return $"Error: tool '{toolName}' not found.";
        return JsonSerializer.Serialize(def, PrettyJson);
    }

    [Description("Adds a new tool or replaces an existing one. The toolJson must be a valid ToolDefinition (matching the bridge schema).")]
    public string upsert_tool(
        [Description("Tool name (key in the tools map).")] string toolName,
        [Description("JSON body of the tool definition: description, parameters, fixedParameters, agent, session, signaling.")] string toolJson)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return "Error: toolName is required.";
        ToolDefinition? def;
        try { def = JsonSerializer.Deserialize<ToolDefinition>(toolJson); }
        catch (Exception ex) { return $"Error: invalid tool JSON — {ex.Message}"; }
        if (def == null) return "Error: tool JSON deserialized to null.";

        _staging.Staged.Tools[toolName] = def;
        return $"Staged: tool '{toolName}' upserted.";
    }

    [Description("Removes a tool from the staged configuration.")]
    public string delete_tool(
        [Description("Exact tool name to remove.")] string toolName)
    {
        return _staging.Staged.Tools.Remove(toolName)
            ? $"Staged: tool '{toolName}' removed."
            : $"Error: tool '{toolName}' not found.";
    }

    [Description("Lists all agent tool sources (MCP servers) in the staged configuration.")]
    public string list_agent_tool_sources()
    {
        var items = _staging.Staged.AgentToolSources
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new { name = kv.Key, type = kv.Value.Type })
            .ToArray();
        return JsonSerializer.Serialize(items, PrettyJson);
    }

    [Description("Returns the full JSON of one agent tool source.")]
    public string get_agent_tool_source(
        [Description("Exact source name.")] string sourceName)
    {
        if (!_staging.Staged.AgentToolSources.TryGetValue(sourceName, out var src))
            return $"Error: agent tool source '{sourceName}' not found.";
        return JsonSerializer.Serialize(src, PrettyJson);
    }

    [Description("Adds or replaces an agent tool source. The sourceJson must be a valid AgentToolSource (stdio/http/sse/websocket).")]
    public string upsert_agent_tool_source(
        [Description("Source name (key in agentToolSources).")] string sourceName,
        [Description("JSON body: type plus command/args (stdio) or url/headers (http, sse, websocket).")] string sourceJson)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return "Error: sourceName is required.";
        AgentToolSource? src;
        try { src = JsonSerializer.Deserialize<AgentToolSource>(sourceJson); }
        catch (Exception ex) { return $"Error: invalid source JSON — {ex.Message}"; }
        if (src == null) return "Error: source JSON deserialized to null.";

        _staging.Staged.AgentToolSources[sourceName] = src;
        return $"Staged: agent tool source '{sourceName}' upserted.";
    }

    [Description("Removes an agent tool source from the staged configuration.")]
    public string delete_agent_tool_source(
        [Description("Exact source name to remove.")] string sourceName)
    {
        return _staging.Staged.AgentToolSources.Remove(sourceName)
            ? $"Staged: agent tool source '{sourceName}' removed."
            : $"Error: agent tool source '{sourceName}' not found.";
    }

    [Description("Returns the staged defaults section (agent, session, signaling defaults).")]
    public string get_defaults()
        => JsonSerializer.Serialize(_staging.Staged.Defaults, PrettyJson);

    [Description("Replaces the defaults section wholesale with the provided JSON body.")]
    public string set_defaults(
        [Description("JSON body matching the DefaultsConfiguration shape.")] string defaultsJson)
    {
        DefaultsConfiguration? next;
        try { next = JsonSerializer.Deserialize<DefaultsConfiguration>(defaultsJson); }
        catch (Exception ex) { return $"Error: invalid defaults JSON — {ex.Message}"; }
        if (next == null) return "Error: defaults JSON deserialized to null.";

        _staging.Staged.Defaults = next;
        return "Staged: defaults updated.";
    }

    [Description("Returns the staged server section.")]
    public string get_server()
        => JsonSerializer.Serialize(_staging.Staged.Server, PrettyJson);

    [Description("Replaces the server section wholesale with the provided JSON body.")]
    public string set_server(
        [Description("JSON body matching the ServerConfiguration shape (port, basePath, bindAddress).")] string serverJson)
    {
        ServerConfiguration? next;
        try { next = JsonSerializer.Deserialize<ServerConfiguration>(serverJson); }
        catch (Exception ex) { return $"Error: invalid server JSON — {ex.Message}"; }
        if (next == null) return "Error: server JSON deserialized to null.";

        _staging.Staged.Server = next;
        return "Staged: server updated.";
    }

    [Description("Returns the staged configAgent section (provider/model/reasoningEffort/promptFile for this chat agent itself).")]
    public string get_config_agent()
        => JsonSerializer.Serialize(_staging.Staged.ConfigAgent, PrettyJson);

    [Description("Replaces (or sets, if null) the configAgent section. Pass an empty object {} to clear individual fields; pass 'null' as the JSON to remove the section entirely.")]
    public string set_config_agent(
        [Description("JSON body matching ConfigAgentConfiguration (provider, model, reasoningEffort, promptFile) — or the literal string 'null' to remove.")] string configAgentJson)
    {
        if (string.Equals(configAgentJson?.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            _staging.Staged.ConfigAgent = null;
            return "Staged: configAgent section removed.";
        }
        ConfigAgentConfiguration? next;
        try { next = JsonSerializer.Deserialize<ConfigAgentConfiguration>(configAgentJson ?? "{}"); }
        catch (Exception ex) { return $"Error: invalid configAgent JSON — {ex.Message}"; }
        if (next == null) return "Error: configAgent JSON deserialized to null (use 'null' to clear).";

        _staging.Staged.ConfigAgent = next;
        return "Staged: configAgent updated.";
    }

    // ---- Fine-grained tool edits (avoid full-body round-trip) ----

    [Description("Sets or clears the description of an existing tool. Pass empty string to clear.")]
    public string set_tool_description(
        [Description("Exact tool name.")] string toolName,
        [Description("New description text (or empty to clear).")] string description)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        tool.Description = string.IsNullOrEmpty(description) ? null : description;
        return $"Staged: tool '{toolName}' description set.";
    }

    [Description("Replaces the 'agent' subsection of one tool (or removes it with 'null'). Touches only tools.<toolName>.agent.")]
    public string set_tool_agent(
        [Description("Exact tool name.")] string toolName,
        [Description("JSON body matching AgentConfiguration (provider, model, reasoningEffort, tools, promptFile) — or 'null' to remove.")] string agentJson)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";

        if (string.Equals(agentJson?.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            tool.Agent = null;
            return $"Staged: tool '{toolName}' agent section removed.";
        }
        AgentConfiguration? next;
        try { next = JsonSerializer.Deserialize<AgentConfiguration>(agentJson ?? "{}"); }
        catch (Exception ex) { return $"Error: invalid agent JSON — {ex.Message}"; }
        if (next == null) return "Error: agent JSON deserialized to null.";

        tool.Agent = next;
        return $"Staged: tool '{toolName}' agent section set.";
    }

    [Description("Replaces the 'session' subsection of one tool (or removes it with 'null').")]
    public string set_tool_session(
        [Description("Exact tool name.")] string toolName,
        [Description("JSON body matching SessionConfiguration — or 'null' to remove.")] string sessionJson)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";

        if (string.Equals(sessionJson?.Trim(), "null", StringComparison.OrdinalIgnoreCase))
        {
            tool.Session = null;
            return $"Staged: tool '{toolName}' session section removed.";
        }
        SessionConfiguration? next;
        try { next = JsonSerializer.Deserialize<SessionConfiguration>(sessionJson ?? "{}"); }
        catch (Exception ex) { return $"Error: invalid session JSON — {ex.Message}"; }
        if (next == null) return "Error: session JSON deserialized to null.";

        tool.Session = next;
        return $"Staged: tool '{toolName}' session section set.";
    }

    [Description("Adds or replaces a single parameter of a tool. Use remove_tool_parameter to delete.")]
    public string set_tool_parameter(
        [Description("Exact tool name.")] string toolName,
        [Description("Parameter name (key).")] string paramName,
        [Description("JSON body matching ParameterDefinition (type, description?, required?, default?, enum?, items?, pattern?, minimum?, maximum?).")] string parameterJson)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        if (string.IsNullOrWhiteSpace(paramName))
            return "Error: paramName is required.";
        ParameterDefinition? next;
        try { next = JsonSerializer.Deserialize<ParameterDefinition>(parameterJson ?? "{}"); }
        catch (Exception ex) { return $"Error: invalid parameter JSON — {ex.Message}"; }
        if (next == null) return "Error: parameter JSON deserialized to null.";

        tool.Parameters[paramName] = next;
        return $"Staged: tool '{toolName}' parameter '{paramName}' set.";
    }

    [Description("Removes a parameter from a tool.")]
    public string remove_tool_parameter(
        [Description("Exact tool name.")] string toolName,
        [Description("Parameter name (key).")] string paramName)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        return tool.Parameters.Remove(paramName)
            ? $"Staged: tool '{toolName}' parameter '{paramName}' removed."
            : $"Error: parameter '{paramName}' not found on tool '{toolName}'.";
    }

    [Description("Adds or replaces a fixed parameter value on a tool (a value always passed to the external call).")]
    public string set_tool_fixed_parameter(
        [Description("Exact tool name.")] string toolName,
        [Description("Fixed parameter name (key).")] string paramName,
        [Description("JSON value (string, number, boolean, object, or array) to bind.")] string valueJson)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        if (string.IsNullOrWhiteSpace(paramName))
            return "Error: paramName is required.";
        JsonElement value;
        try { value = JsonDocument.Parse(valueJson ?? "null").RootElement.Clone(); }
        catch (Exception ex) { return $"Error: invalid JSON value — {ex.Message}"; }

        tool.FixedParameters[paramName] = value;
        return $"Staged: tool '{toolName}' fixed parameter '{paramName}' set.";
    }

    [Description("Removes a fixed parameter from a tool.")]
    public string remove_tool_fixed_parameter(
        [Description("Exact tool name.")] string toolName,
        [Description("Fixed parameter name (key).")] string paramName)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        return tool.FixedParameters.Remove(paramName)
            ? $"Staged: tool '{toolName}' fixed parameter '{paramName}' removed."
            : $"Error: fixed parameter '{paramName}' not found on tool '{toolName}'.";
    }

    // ---- Signaling entries (fine-grained) ----

    [Description("Lists all signaling tool entries for a given tool (name + isBlocking).")]
    public string list_signaling_tools(
        [Description("Exact tool name.")] string toolName)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        var entries = (tool.Signaling?.Tools ?? new List<SignalingToolEntry>())
            .Select(e => new { e.Name, e.IsBlocking, e.ResponseFormat, e.OutgoingFormat })
            .ToArray();
        return JsonSerializer.Serialize(entries, PrettyJson);
    }

    [Description("Returns the full JSON of one signaling entry on a tool.")]
    public string get_signaling_tool(
        [Description("Exact tool name.")] string toolName,
        [Description("Signal entry name.")] string signalName)
    {
        var entry = FindSignalingEntry(toolName, signalName, out var err);
        return entry != null ? JsonSerializer.Serialize(entry, PrettyJson) : err!;
    }

    [Description("Adds or replaces a single signaling entry on a tool (matched by 'name' field in the JSON body). Creates the signaling section if absent.")]
    public string upsert_signaling_tool(
        [Description("Exact tool name.")] string toolName,
        [Description("JSON body matching SignalingToolEntry — must include 'name'.")] string entryJson)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        SignalingToolEntry? entry;
        try { entry = JsonSerializer.Deserialize<SignalingToolEntry>(entryJson ?? "{}"); }
        catch (Exception ex) { return $"Error: invalid signaling entry JSON — {ex.Message}"; }
        if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            return "Error: signaling entry must have a non-empty 'name'.";

        tool.Signaling ??= new SignalingConfiguration();
        var idx = tool.Signaling.Tools.FindIndex(e => string.Equals(e.Name, entry.Name, StringComparison.Ordinal));
        if (idx >= 0) tool.Signaling.Tools[idx] = entry;
        else tool.Signaling.Tools.Add(entry);
        return $"Staged: tool '{toolName}' signaling entry '{entry.Name}' upserted.";
    }

    [Description("Removes a single signaling entry from a tool.")]
    public string remove_signaling_tool(
        [Description("Exact tool name.")] string toolName,
        [Description("Signal entry name to remove.")] string signalName)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        if (tool.Signaling?.Tools == null) return "Error: tool has no signaling section.";
        var removed = tool.Signaling.Tools.RemoveAll(e => string.Equals(e.Name, signalName, StringComparison.Ordinal));
        return removed > 0
            ? $"Staged: tool '{toolName}' signaling entry '{signalName}' removed."
            : $"Error: signaling entry '{signalName}' not found on tool '{toolName}'.";
    }

    [Description("Sets the keepaliveIntervalSeconds on a tool's signaling section (creates the section if absent).")]
    public string set_tool_signaling_keepalive(
        [Description("Exact tool name.")] string toolName,
        [Description("Keepalive interval in seconds (>=1).")] int seconds)
    {
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
            return $"Error: tool '{toolName}' not found.";
        if (seconds < 1) return "Error: seconds must be >= 1.";
        tool.Signaling ??= new SignalingConfiguration();
        tool.Signaling.KeepaliveIntervalSeconds = seconds;
        return $"Staged: tool '{toolName}' keepaliveIntervalSeconds set to {seconds}.";
    }

    // ---- Validation ----

    [Description("Validates the currently staged configuration for cross-reference issues (missing agent tool sources, missing prompt files, unknown signaling tool names, etc.). Returns a JSON array of issue strings; empty array means clean.")]
    public string validate_staged_config()
    {
        var issues = new List<string>();
        var s = _staging.Staged;
        var sourceNames = new HashSet<string>(s.AgentToolSources.Keys, StringComparer.Ordinal);
        var promptPaths = new HashSet<string>(_staging.ListAllPromptPaths(), StringComparer.OrdinalIgnoreCase);

        void CheckPromptFile(string? file, string location)
        {
            if (string.IsNullOrWhiteSpace(file)) return;
            var n = file.Replace('\\', '/').TrimStart('/');
            if (n.StartsWith("./", StringComparison.Ordinal)) n = n.Substring(2);
            if (n.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase)) n = n.Substring("prompts/".Length);
            if (!promptPaths.Contains(n))
                issues.Add($"{location}: prompt file '{file}' is not staged or on disk.");
        }

        foreach (var (toolName, tool) in s.Tools)
        {
            if (tool.Agent?.Tools != null)
            {
                foreach (var src in tool.Agent.Tools)
                    if (!sourceNames.Contains(src))
                        issues.Add($"tools.{toolName}.agent.tools: source '{src}' is not defined in agentToolSources.");
            }
            CheckPromptFile(tool.Agent?.PromptFile, $"tools.{toolName}.agent.promptFile");

            if (tool.Signaling?.Tools != null)
            {
                foreach (var entry in tool.Signaling.Tools)
                {
                    CheckPromptFile(entry.ResponsePromptFile, $"tools.{toolName}.signaling[{entry.Name}].responsePromptFile");
                    CheckPromptFile(entry.OutgoingPromptFile, $"tools.{toolName}.signaling[{entry.Name}].outgoingPromptFile");

                    if (string.Equals(entry.OutgoingFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(entry.OutgoingPromptFile))
                        issues.Add($"tools.{toolName}.signaling[{entry.Name}]: outgoingFormat=markdown requires outgoingPromptFile.");

                    if (entry.IsBlocking
                        && string.Equals(entry.ResponseFormat, "markdown", StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(entry.ResponsePromptFile))
                        issues.Add($"tools.{toolName}.signaling[{entry.Name}]: blocking + responseFormat=markdown requires responsePromptFile.");
                }
            }

            if (tool.Session?.ReferenceIdParameter != null
                && !tool.Parameters.ContainsKey(tool.Session.ReferenceIdParameter)
                && !tool.FixedParameters.ContainsKey(tool.Session.ReferenceIdParameter))
                issues.Add($"tools.{toolName}.session.referenceIdParameter='{tool.Session.ReferenceIdParameter}' is not a declared parameter.");
        }

        CheckPromptFile(s.ConfigAgent?.PromptFile, "configAgent.promptFile");
        if (s.Defaults?.Agent?.Tools != null)
            foreach (var src in s.Defaults.Agent.Tools)
                if (!sourceNames.Contains(src))
                    issues.Add($"defaults.agent.tools: source '{src}' is not defined in agentToolSources.");

        return JsonSerializer.Serialize(issues, PrettyJson);
    }

    private SignalingToolEntry? FindSignalingEntry(string toolName, string signalName, out string? error)
    {
        error = null;
        if (!_staging.Staged.Tools.TryGetValue(toolName, out var tool))
        {
            error = $"Error: tool '{toolName}' not found.";
            return null;
        }
        var entry = tool.Signaling?.Tools?.FirstOrDefault(e => string.Equals(e.Name, signalName, StringComparison.Ordinal));
        if (entry == null)
        {
            error = $"Error: signaling entry '{signalName}' not found on tool '{toolName}'.";
            return null;
        }
        return entry;
    }

    [Description("Lists all prompt file paths currently present (baseline + staged additions, minus staged deletions).")]
    public string list_prompt_files()
    {
        var paths = _staging.ListAllPromptPaths();
        return JsonSerializer.Serialize(paths, PrettyJson);
    }

    [Description("Reads the staged content of a prompt file (falls back to baseline). Returns 'Error: not found' if the file does not exist in either.")]
    public string read_prompt_file(
        [Description("Relative path under prompts/, e.g. 'code-review.md'.")] string path)
    {
        var content = _staging.GetPromptContent(path);
        return content ?? $"Error: prompt file '{path}' not found.";
    }

    [Description("Stages a write (create or overwrite) of a prompt file. Path must end in .md and live under prompts/.")]
    public string write_prompt_file(
        [Description("Relative path under prompts/, e.g. 'new-tool.md'.")] string path,
        [Description("Full file contents.")] string content)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Error: path is required.";
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return "Error: prompt file must end in .md.";
        _staging.StagePromptWrite(path, content ?? string.Empty);
        return $"Staged: prompt '{path}' written ({(content ?? string.Empty).Length} chars).";
    }

    [Description("Stages deletion of a prompt file.")]
    public string delete_prompt_file(
        [Description("Relative path under prompts/.")] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Error: path is required.";
        _staging.StagePromptDelete(path);
        return $"Staged: prompt '{path}' marked for deletion.";
    }

    [Description("Returns the current set of pending staged changes (config + prompt file diffs) — useful for confirming before concluding.")]
    public string list_pending_changes()
    {
        var diff = _staging.ComputeDiff();
        var summary = new
        {
            configChanges = diff.ConfigChanges.Select(c => new { c.Section, c.Key, kind = c.Kind.ToString() }).ToArray(),
            promptChanges = diff.PromptChanges.Select(p => new { p.RelativePath, kind = p.Kind.ToString() }).ToArray(),
            isEmpty = diff.IsEmpty,
        };
        return JsonSerializer.Serialize(summary, PrettyJson);
    }

    /// <summary>
    /// Returns all tool methods as <see cref="Delegate"/> instances so they can be
    /// wrapped with <c>AIFunctionFactory.Create</c> and attached to a Copilot session.
    /// </summary>
    public IEnumerable<Delegate> AsDelegates() => new Delegate[]
    {
        get_config_summary,
        list_tools,
        get_tool,
        upsert_tool,
        delete_tool,
        set_tool_description,
        set_tool_agent,
        set_tool_session,
        set_tool_parameter,
        remove_tool_parameter,
        set_tool_fixed_parameter,
        remove_tool_fixed_parameter,
        list_signaling_tools,
        get_signaling_tool,
        upsert_signaling_tool,
        remove_signaling_tool,
        set_tool_signaling_keepalive,
        list_agent_tool_sources,
        get_agent_tool_source,
        upsert_agent_tool_source,
        delete_agent_tool_source,
        get_defaults,
        set_defaults,
        get_server,
        set_server,
        get_config_agent,
        set_config_agent,
        list_prompt_files,
        read_prompt_file,
        write_prompt_file,
        delete_prompt_file,
        list_pending_changes,
        validate_staged_config,
    };
}
