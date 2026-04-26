using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Signaling;

namespace Praetorium.Bridge.Sessions;

/// <summary>
/// Implementation of session management with lifecycle tracking and pooling.
/// Uses IAgentSession abstraction for provider-agnostic agent management.
/// </summary>
public class SessionManager : ISessionManager, IHostedService
{
    private readonly ISessionStore _sessionStore;
    private readonly ISignalRegistry _signalRegistry;
    private readonly IAgentProvider _agentProvider;
    private readonly IBridgeHooks _hooks;
    private readonly ILogger<SessionManager> _logger;
    private readonly ConcurrentDictionary<string, IAgentSession> _activeAgents = new();
    private readonly ConcurrentDictionary<string, Task<string>> _runningTurns = new();
    private static readonly TimeSpan OrphanDetectionInterval = TimeSpan.FromMinutes(1);
    private PeriodicTimer? _orphanDetectionTimer;
    private CancellationTokenSource? _orphanDetectionCts;
    private Task? _orphanDetectionLoop;

    /// <summary>
    /// Initializes a new instance of the SessionManager class.
    /// </summary>
    /// <param name="sessionStore">The session store for persistence.</param>
    /// <param name="signalRegistry">The signal registry for session signaling.</param>
    /// <param name="agentProvider">The agent provider for spawning agents.</param>
    /// <param name="hooks">The bridge hooks for lifecycle events.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public SessionManager(
        ISessionStore sessionStore,
        ISignalRegistry signalRegistry,
        IAgentProvider agentProvider,
        IBridgeHooks hooks,
        ILogger<SessionManager> logger)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _signalRegistry = signalRegistry ?? throw new ArgumentNullException(nameof(signalRegistry));
        _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the orphan detection background loop.
    /// </summary>
    public void StartOrphanDetection()
    {
        if (_orphanDetectionLoop != null)
            return;

        _orphanDetectionCts = new CancellationTokenSource();
        _orphanDetectionTimer = new PeriodicTimer(OrphanDetectionInterval);
        _orphanDetectionLoop = Task.Run(() => OrphanDetectionLoopAsync(_orphanDetectionTimer, _orphanDetectionCts.Token));
    }

    /// <summary>
    /// Stops the orphan detection background loop and waits for it to complete.
    /// </summary>
    public async Task StopOrphanDetectionAsync()
    {
        var loop = _orphanDetectionLoop;
        var cts = _orphanDetectionCts;
        var timer = _orphanDetectionTimer;

        _orphanDetectionLoop = null;
        _orphanDetectionCts = null;
        _orphanDetectionTimer = null;

        if (cts != null)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        timer?.Dispose();

        if (loop != null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Orphan detection loop terminated with an error."); }
        }

        cts?.Dispose();
    }

    private async Task OrphanDetectionLoopAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await DetectAndCleanupOrphanSessionsAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during orphan session detection.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    /// <summary>
    /// Gets an existing session or creates a new one based on the session mode and configuration.
    /// </summary>
    public async Task<(SessionInfo Session, bool IsNew)> GetOrCreateSessionAsync(
        string toolName,
        string? referenceId,
        string? connectionId,
        SessionMode mode,
        AgentConfiguration agentConfig,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty.", nameof(toolName));

        if (agentConfig == null)
            throw new ArgumentNullException(nameof(agentConfig));

        SessionInfo? existingSession = null;

        switch (mode)
        {
            case SessionMode.PerReference:
                if (string.IsNullOrEmpty(referenceId))
                    throw new ArgumentException("Reference ID is required for PerReference mode.", nameof(referenceId));
                existingSession = await _sessionStore.GetByReferenceAsync(toolName, referenceId, ct)
                    .ConfigureAwait(false);
                break;

            case SessionMode.PerConnection:
                if (string.IsNullOrEmpty(connectionId))
                    throw new ArgumentException("Connection ID is required for PerConnection mode.", nameof(connectionId));
                existingSession = await _sessionStore.GetByConnectionAsync(toolName, connectionId, ct)
                    .ConfigureAwait(false);
                break;

            case SessionMode.Global:
                existingSession = await _sessionStore.GetGlobalAsync(toolName, ct)
                    .ConfigureAwait(false);
                break;
        }

        if (existingSession != null)
        {
            if (existingSession.State == SessionState.Crashed)
            {
                var newSession = await SpawnNewSessionAsync(toolName, referenceId, connectionId, agentConfig, ct)
                    .ConfigureAwait(false);
                return (newSession, true);
            }

            if (existingSession.State == SessionState.Pooled)
            {
                existingSession.State = SessionState.Active;
                existingSession.ConnectionId = connectionId;
                existingSession.LastActivityAt = DateTimeOffset.UtcNow;
                await _sessionStore.SetAsync(existingSession, ct).ConfigureAwait(false);

                var hookContext = new SessionContext(
                    Guid.NewGuid().ToString(),
                    existingSession.SessionId,
                    toolName,
                    referenceId);

                await _hooks.OnSessionWokenAsync(hookContext, ct).ConfigureAwait(false);

                return (existingSession, false);
            }

            existingSession.LastActivityAt = DateTimeOffset.UtcNow;
            await _sessionStore.SetAsync(existingSession, ct).ConfigureAwait(false);
            return (existingSession, false);
        }

        var newSessionInfo = await SpawnNewSessionAsync(toolName, referenceId, connectionId, agentConfig, ct)
            .ConfigureAwait(false);
        return (newSessionInfo, true);
    }

    /// <summary>
    /// Resets a session by stopping the current agent and moving it to pooled state.
    /// </summary>
    public async Task ResetSessionAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for reset.", sessionId);
            return;
        }

        // Remove the agent session from tracking and dispose the underlying provider resources.
        await DisposeActiveAgentAsync(sessionId).ConfigureAwait(false);
        _runningTurns.TryRemove(sessionId, out _);

        session.State = SessionState.Pooled;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        session.ToolCallCount = 0;
        session.CurrentQuestion = null;
        await _sessionStore.SetAsync(session, ct).ConfigureAwait(false);

        _signalRegistry.RemoveSession(sessionId);

        var hookContext = new SessionDroppedContext(
            Guid.NewGuid().ToString(),
            sessionId,
            session.ToolName,
            SessionDropReason.Reset,
            session.ReferenceId);

        await _hooks.OnSessionDroppedAsync(hookContext, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a session to the pooled state for reuse.
    /// </summary>
    public async Task PoolSessionAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for pooling.", sessionId);
            return;
        }

        await DisposeActiveAgentAsync(sessionId).ConfigureAwait(false);
        _runningTurns.TryRemove(sessionId, out _);

        session.State = SessionState.Pooled;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await _sessionStore.SetAsync(session, ct).ConfigureAwait(false);

        var hookContext = new SessionContext(
            Guid.NewGuid().ToString(),
            sessionId,
            session.ToolName,
            session.ReferenceId);

        await _hooks.OnSessionPooledAsync(hookContext, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the state of a session.
    /// </summary>
    public async Task UpdateStateAsync(string sessionId, SessionState state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for state update.", sessionId);
            return;
        }

        session.State = state;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await _sessionStore.SetAsync(session, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a session as crashed due to an unhandled exception.
    /// </summary>
    public async Task MarkCrashedAsync(string sessionId, Exception ex, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        if (ex == null)
            throw new ArgumentNullException(nameof(ex));

        var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for crash handling.", sessionId);
            return;
        }

        await DisposeActiveAgentAsync(sessionId).ConfigureAwait(false);
        _runningTurns.TryRemove(sessionId, out _);

        session.State = SessionState.Crashed;
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await _sessionStore.SetAsync(session, ct).ConfigureAwait(false);

        _signalRegistry.RemoveSession(sessionId);

        var hookContext = new AgentCrashedContext(
            Guid.NewGuid().ToString(),
            sessionId,
            session.ToolName,
            ex,
            session.ReferenceId);

        await _hooks.OnAgentCrashedAsync(hookContext, ct).ConfigureAwait(false);

        _logger.LogError(ex, "Session {SessionId} crashed.", sessionId);
    }

    /// <summary>
    /// Removes a session completely from the store.
    /// </summary>
    public async Task RemoveSessionAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        await DisposeActiveAgentAsync(sessionId).ConfigureAwait(false);
        _runningTurns.TryRemove(sessionId, out _);
        _signalRegistry.RemoveSession(sessionId);
        await _sessionStore.RemoveAsync(sessionId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all active sessions (those not in Pooled or Crashed state).
    /// </summary>
    public async Task<IReadOnlyList<SessionInfo>> GetActiveSessionsAsync(CancellationToken ct)
    {
        var allSessions = await _sessionStore.GetAllAsync(ct).ConfigureAwait(false);
        return allSessions
            .Where(s => s.State != SessionState.Pooled && s.State != SessionState.Crashed)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    public async Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(CancellationToken ct)
    {
        return await _sessionStore.GetAllAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Notifies the session manager of a caller disconnect event.
    /// </summary>
    public async Task NotifyDisconnectAsync(string connectionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));

        _signalRegistry.SignalDisconnect(connectionId);

        var allSessions = await _sessionStore.GetAllAsync(ct).ConfigureAwait(false);
        var sessionsForConnection = allSessions
            .Where(s => s.ConnectionId == connectionId)
            .ToList();

        foreach (var session in sessionsForConnection)
        {
            if (session.State != SessionState.Crashed && session.State != SessionState.Pooled)
            {
                await DisposeActiveAgentAsync(session.SessionId).ConfigureAwait(false);
                _runningTurns.TryRemove(session.SessionId, out _);
                session.State = SessionState.Crashed;
                session.LastActivityAt = DateTimeOffset.UtcNow;
                await _sessionStore.SetAsync(session, ct).ConfigureAwait(false);

                var hookContext = new SessionDroppedContext(
                    Guid.NewGuid().ToString(),
                    session.SessionId,
                    session.ToolName,
                    SessionDropReason.Disconnect,
                    session.ReferenceId);

                await _hooks.OnSessionDroppedAsync(hookContext, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Spawns a new session (placeholder for agent spawning).
    /// The actual agent is spawned by the tool dispatcher after prompt resolution.
    /// </summary>
    private async Task<SessionInfo> SpawnNewSessionAsync(
        string toolName,
        string? referenceId,
        string? connectionId,
        AgentConfiguration agentConfig,
        CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;

        var sessionInfo = new SessionInfo(
            sessionId,
            toolName,
            SessionState.Spawning,
            now,
            referenceId,
            connectionId,
            agentConfig.Model);

        await _sessionStore.SetAsync(sessionInfo, ct).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(connectionId))
        {
            _signalRegistry.RegisterConnectionBinding(sessionId, connectionId);
        }

        sessionInfo.State = SessionState.Active;
        await _sessionStore.SetAsync(sessionInfo, ct).ConfigureAwait(false);

        return sessionInfo;
    }

    /// <summary>
    /// Detects and cleans up orphaned sessions that have been inactive for too long.
    /// </summary>
    private async Task DetectAndCleanupOrphanSessionsAsync()
    {
        var allSessions = await _sessionStore.GetAllAsync(CancellationToken.None)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var timeoutDuration = TimeSpan.FromMinutes(30);

        var orphanedSessions = allSessions
            .Where(s => s.State == SessionState.Spawning ||
                (s.State == SessionState.Active && now - s.LastActivityAt > timeoutDuration))
            .ToList();

        foreach (var session in orphanedSessions)
        {
            _logger.LogWarning(
                "Detected orphaned session {SessionId} in state {State}. Removing.",
                session.SessionId,
                session.State);

            await DisposeActiveAgentAsync(session.SessionId).ConfigureAwait(false);
            _runningTurns.TryRemove(session.SessionId, out _);
            _signalRegistry.RemoveSession(session.SessionId);
            await _sessionStore.RemoveAsync(session.SessionId, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Removes the active agent for the given session and disposes its underlying
    /// provider resources (SDK session handle, loopback MCP registration, etc.).
    /// Disposal failures are logged but never thrown so they cannot interfere with
    /// session-state bookkeeping by the caller.
    /// </summary>
    private async Task DisposeActiveAgentAsync(string sessionId)
    {
        if (!_activeAgents.TryRemove(sessionId, out var agent))
            return;

        try
        {
            await agent.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing agent for session {SessionId}.", sessionId);
        }
    }

    /// <summary>
    /// Gets the active agent session for a given session ID.
    /// </summary>
    public IAgentSession? GetActiveAgent(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        _activeAgents.TryGetValue(sessionId, out var agent);
        return agent;
    }

    /// <summary>
    /// Creates an agent session for an existing session.
    /// The provider handles session management and tool calling internally.
    /// </summary>
    /// <param name="sessionId">The session ID to create the agent for.</param>
    /// <param name="toolName">The tool name the agent is handling.</param>
    /// <param name="prompt">The system prompt for the agent.</param>
    /// <param name="agentConfig">The agent configuration.</param>
    /// <param name="toolSources">The MCP tool sources for the agent.</param>
    /// <param name="signalingTools">The signaling tool definitions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created agent session.</returns>
    public async Task<IAgentSession> CreateAgentAsync(
        string sessionId,
        string toolName,
        string systemPrompt,
        AgentConfiguration agentConfig,
        List<AgentToolSource> toolSources,
        List<SignalingToolDefinition> signalingTools,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty.", nameof(toolName));
        if (agentConfig == null)
            throw new ArgumentNullException(nameof(agentConfig));

        var agentContext = new AgentContext(
            toolName,
            systemPrompt,
            agentConfig,
            toolSources ?? new(),
            signalingTools ?? new());

        var aiAgent = await _agentProvider.CreateAgentAsync(agentContext, ct).ConfigureAwait(false);
        _activeAgents[sessionId] = aiAgent;

        var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session != null)
        {
            var hookContext = new SessionContext(
                Guid.NewGuid().ToString(),
                sessionId,
                toolName,
                session.ReferenceId,
                systemPrompt);

            await _hooks.OnSessionSpawnedAsync(hookContext, ct).ConfigureAwait(false);
        }

        _logger.LogInformation("Agent session created for session {SessionId}", sessionId);
        return aiAgent;
    }

    /// <inheritdoc/>
    public void SetRunningTurn(string sessionId, Task<string> turnTask)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));
        if (turnTask == null)
            throw new ArgumentNullException(nameof(turnTask));

        _runningTurns[sessionId] = turnTask;
    }

    /// <inheritdoc/>
    public Task<string>? GetRunningTurn(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        _runningTurns.TryGetValue(sessionId, out var turn);
        return turn;
    }

    /// <inheritdoc/>
    public void ClearRunningTurn(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        _runningTurns.TryRemove(sessionId, out _);
    }

    /// <inheritdoc/>
    public async Task<bool> CancelSessionWaitsAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        var session = await _sessionStore.GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session == null)
        {
            _logger.LogWarning("Session {SessionId} not found for cancel.", sessionId);
            return false;
        }

        _signalRegistry.CancelWaiters(sessionId);
        session.LastActivityAt = DateTimeOffset.UtcNow;
        await _sessionStore.SetAsync(session, ct).ConfigureAwait(false);

        _logger.LogInformation("Cancel requested for session {SessionId}; reset delivered on both channels.", sessionId);
        return true;
    }

    /// <summary>
    /// Starts the hosted service and begins orphan detection.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SessionManager hosted service starting.");
        StartOrphanDetection();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the hosted service and halts orphan detection.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SessionManager hosted service stopping.");
        await StopOrphanDetectionAsync().ConfigureAwait(false);
    }
}
