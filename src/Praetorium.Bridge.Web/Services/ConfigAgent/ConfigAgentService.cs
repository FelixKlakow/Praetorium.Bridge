using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using BridgeConfigurationProvider = Praetorium.Bridge.Configuration.IConfigurationProvider;

namespace Praetorium.Bridge.Web.Services.ConfigAgent;

public enum ConfigAgentMessageRole { User, Assistant, System }

public sealed record ConfigAgentMessage(
    ConfigAgentMessageRole Role,
    string Content,
    DateTimeOffset Timestamp);

/// <summary>
/// Per-user (per Blazor circuit) Config Agent session. Holds the chat history,
/// the underlying Copilot session, and the staged configuration changes.
/// </summary>
public sealed class ConfigAgentService : IAsyncDisposable
{
    private readonly CopilotClient _copilot;
    private readonly IAgentProvider _agentProvider;
    private readonly BridgeConfigurationProvider _configProvider;
    private readonly ConfigurationService _configService;
    private readonly ILogger<ConfigAgentService> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ConfigAgentMessage> _messages = new();

    private ConfigStaging? _staging;
    private CopilotSession? _session;
    private IDisposable? _subscription;
    private TurnState? _currentTurn;
    private string? _activeModel;

    private sealed class TurnState
    {
        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StringBuilder Buffer { get; } = new();
    }

    public ConfigAgentService(
        CopilotClient copilot,
        IAgentProvider agentProvider,
        BridgeConfigurationProvider configProvider,
        ConfigurationService configService,
        ILogger<ConfigAgentService> logger)
    {
        _copilot = copilot ?? throw new ArgumentNullException(nameof(copilot));
        _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event Action? StateChanged;

    public IReadOnlyList<ConfigAgentMessage> Messages => _messages;

    public string? ActiveModel => _activeModel;

    public bool HasSession => _session != null;

    public ConfigChangeSet GetChangeSet()
        => _staging?.ComputeDiff() ?? new ConfigChangeSet
        {
            ConfigChanges = Array.Empty<ConfigChange>(),
            PromptChanges = Array.Empty<PromptChange>(),
        };

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct)
        => _agentProvider.GetAvailableModelsAsync(ct);

    public string ResolveDefaultModel()
    {
        var cfg = _configProvider.Configuration.ConfigAgent;
        return cfg?.Model
            ?? _configProvider.Configuration.Defaults?.Agent?.Model
            ?? "claude-sonnet-4.6";
    }

    /// <summary>
    /// Starts a fresh chat — disposes any existing session, resets staging, and initializes
    /// a new Copilot session with the provided model.
    /// </summary>
    public async Task StartNewChatAsync(string model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException("Model is required.", nameof(model));

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await DisposeSessionAsync().ConfigureAwait(false);

            _messages.Clear();
            _staging = await SnapshotLiveIntoStagingAsync(ct).ConfigureAwait(false);
            var tools = new ConfigAgentTools(_staging);
            var systemPrompt = await LoadSystemPromptAsync(ct).ConfigureAwait(false);

            var cfg = new SessionConfig
            {
                Model = model,
                OnPermissionRequest = PermissionHandler.ApproveAll,
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = systemPrompt,
                },
                Tools = tools.AsDelegates().Select(d => AIFunctionFactory.Create(d)).ToArray(),
            };

            var reasoningEffort = _configProvider.Configuration.ConfigAgent?.ReasoningEffort;
            if (!string.IsNullOrEmpty(reasoningEffort) && _agentProvider.SupportsReasoningEffort(model))
                cfg.ReasoningEffort = reasoningEffort;

            _session = await _copilot.CreateSessionAsync(cfg, ct).ConfigureAwait(false);
            _subscription = _session.On(OnSessionEvent);
            _activeModel = model;
            _logger.LogInformation("ConfigAgent session started with model '{Model}'.", model);
        }
        finally
        {
            _gate.Release();
        }

        StateChanged?.Invoke();
    }

    public async Task<string> SendAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            throw new ArgumentException("Message cannot be empty.", nameof(userMessage));

        if (_session == null || _staging == null)
            await StartNewChatAsync(ResolveDefaultModel(), ct).ConfigureAwait(false);

        _messages.Add(new ConfigAgentMessage(ConfigAgentMessageRole.User, userMessage, DateTimeOffset.Now));
        StateChanged?.Invoke();

        string reply;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        TurnState turn;
        try
        {
            turn = new TurnState();
            _currentTurn = turn;
            await _session!.SendAsync(new MessageOptions { Prompt = userMessage }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _currentTurn = null;
            _gate.Release();
            _logger.LogError(ex, "ConfigAgent SendAsync failed.");
            reply = $"[error] {ex.Message}";
            _messages.Add(new ConfigAgentMessage(ConfigAgentMessageRole.Assistant, reply, DateTimeOffset.Now));
            StateChanged?.Invoke();
            return reply;
        }

        using var ctr = ct.Register(() => turn.Completion.TrySetCanceled(ct));
        try
        {
            reply = await turn.Completion.Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConfigAgent turn failed.");
            reply = $"[error] {ex.Message}";
        }
        finally
        {
            _currentTurn = null;
            _gate.Release();
        }

        _messages.Add(new ConfigAgentMessage(ConfigAgentMessageRole.Assistant, reply, DateTimeOffset.Now));
        StateChanged?.Invoke();
        return reply;
    }

    private static string FormatToolCall(string toolName, object? rawArguments)
    {
        var argsText = rawArguments switch
        {
            null => null,
            string s => s,
            _ => rawArguments.ToString(),
        };

        if (string.IsNullOrWhiteSpace(argsText))
            return $"→ {toolName}";

        Dictionary<string, string>? args = null;
        try
        {
            using var doc = JsonDocument.Parse(argsText);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                args = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.GetRawText();
            }
        }
        catch { /* fall through to raw */ }

        // Special-case report_intent — show the intent text prominently.
        if (args != null && string.Equals(toolName, "report_intent", StringComparison.Ordinal))
        {
            var intent = args.TryGetValue("intent", out var i) ? i :
                         args.TryGetValue("summary", out var s) ? s :
                         args.Values.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(intent))
                return $"💭 {intent!.Trim()}";
        }

        if (args == null || args.Count == 0)
            return $"→ {toolName}({argsText!.Trim()})";

        var pairs = args.Select(kv =>
        {
            var v = kv.Value ?? string.Empty;
            if (v.Length > 80) v = v.Substring(0, 77) + "…";
            v = v.Replace('\n', ' ').Replace('\r', ' ');
            return $"{kv.Key}: {v}";
        });
        return $"→ {toolName}({string.Join(", ", pairs)})";
    }

    private void OnSessionEvent(SessionEvent evt)
    {
        var turn = _currentTurn;
        switch (evt)
        {
            case AssistantMessageEvent msg:
                var content = msg.Data?.Content;
                if (!string.IsNullOrEmpty(content))
                    turn?.Buffer.AppendLine(content);
                break;
            case ToolExecutionStartEvent tse:
                var name = tse.Data?.ToolName ?? "tool";
                var formatted = FormatToolCall(name, tse.Data?.Arguments);
                _messages.Add(new ConfigAgentMessage(
                    ConfigAgentMessageRole.System,
                    formatted,
                    DateTimeOffset.Now));
                StateChanged?.Invoke();
                break;
            case ToolExecutionCompleteEvent tce:
                if (tce.Data?.Success == false)
                {
                    _messages.Add(new ConfigAgentMessage(
                        ConfigAgentMessageRole.System,
                        $"✗ tool failed: {tce.Data?.Error?.Message ?? "(no error)"}",
                        DateTimeOffset.Now));
                    StateChanged?.Invoke();
                }
                break;
            case SessionErrorEvent err:
                var errText = err.Data?.Message ?? "session error";
                turn?.Completion.TrySetResult(turn.Buffer.Length > 0
                    ? turn.Buffer.ToString().TrimEnd() + $"\n\n[error] {errText}"
                    : $"[error] {errText}");
                break;
            case SessionIdleEvent:
                var text = turn?.Buffer.ToString().TrimEnd() ?? string.Empty;
                turn?.Completion.TrySetResult(string.IsNullOrEmpty(text) ? "(no reply)" : text);
                break;
        }
    }

    public void DiscardPending()
    {
        _staging?.DiscardAll();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Writes the staged configuration and prompt mutations to disk. Leaves the chat
    /// session alive but re-snapshots the staging baseline to match the new live state.
    /// </summary>
    public async Task ApplyAsync(CancellationToken ct)
    {
        if (_staging == null)
            return;

        var diff = _staging.ComputeDiff();
        if (diff.IsEmpty)
            return;

        foreach (var p in diff.PromptChanges)
        {
            if (p.Kind == ChangeKind.Removed)
                _configService.DeletePromptFile(p.RelativePath);
            else
                await _configService.SavePromptContentAsync(p.RelativePath, p.AfterContent ?? string.Empty, ct)
                    .ConfigureAwait(false);
        }

        if (diff.ConfigChanges.Count > 0)
            await _configService.SaveConfigurationAsync(_staging.Staged, ct).ConfigureAwait(false);

        // Re-baseline the SAME staging instance so tools wired into the live Copilot session
        // (which hold a reference to _staging) continue to target fresh state.
        var prompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _configService.ListPromptFiles())
            prompts[name] = await _configService.GetPromptContentAsync(name, ct).ConfigureAwait(false);
        _staging.ReBaseline(_configProvider.Configuration, prompts);
        _messages.Add(new ConfigAgentMessage(
            ConfigAgentMessageRole.System,
            $"Applied {diff.ConfigChanges.Count} config change(s) and {diff.PromptChanges.Count} prompt change(s).",
            DateTimeOffset.Now));
        StateChanged?.Invoke();
    }

    private async Task<ConfigStaging> SnapshotLiveIntoStagingAsync(CancellationToken ct)
    {
        var prompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _configService.ListPromptFiles())
            prompts[name] = await _configService.GetPromptContentAsync(name, ct).ConfigureAwait(false);
        return new ConfigStaging(_configProvider.Configuration, prompts);
    }

    private async Task<string> LoadSystemPromptAsync(CancellationToken ct)
    {
        var cfgPromptFile = _configProvider.Configuration.ConfigAgent?.PromptFile;
        if (!string.IsNullOrWhiteSpace(cfgPromptFile))
        {
            try
            {
                var normalized = cfgPromptFile.Replace('\\', '/').TrimStart('/');
                if (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
                if (normalized.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring("prompts/".Length);
                if (_configService.PromptFileExists(normalized))
                    return await _configService.GetPromptContentAsync(normalized, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ConfigAgent prompt file '{Path}' could not be loaded — using fallback.", cfgPromptFile);
            }
        }

        return """
            You are the Praetorium Bridge Config Agent. You help the user inspect and modify the bridge
            configuration (tools, agent tool sources, defaults, server, configAgent) and its prompt files.
            Every mutation you make is staged — the user reviews and applies it explicitly. Prefer
            `get_config_summary` first to orient yourself, then use `list_pending_changes` after mutations
            to verify. Keep replies terse. Return full JSON only when the user asks for it.
            """;
    }

    private async Task DisposeSessionAsync()
    {
        if (_subscription != null)
        {
            try { _subscription.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "ConfigAgent subscription dispose failed (ignored)."); }
            _subscription = null;
        }
        if (_session != null)
        {
            try { await _session.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogWarning(ex, "ConfigAgent session dispose failed (ignored)."); }
            _session = null;
        }
        _currentTurn?.Completion.TrySetCanceled();
        _currentTurn = null;
        _activeModel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeSessionAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
