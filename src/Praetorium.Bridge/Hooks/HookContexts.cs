using System;
using System.Collections.Generic;
using Praetorium.Bridge.Configuration;

namespace Praetorium.Bridge.Hooks;

/// <summary>
/// Context for a tool invocation event.
/// </summary>
public class ToolInvocationContext : BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the ToolInvocationContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="toolName">Name of the tool being invoked.</param>
    /// <param name="parameters">Parameters passed to the tool.</param>
    /// <param name="connectionId">ID of the connection making the invocation.</param>
    public ToolInvocationContext(string correlationId, string toolName, Dictionary<string, object?> parameters, string connectionId)
        : base(correlationId)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
    }

    /// <summary>
    /// Gets the name of the tool being invoked.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the parameters passed to the tool.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; }

    /// <summary>
    /// Gets the connection ID of the client.
    /// </summary>
    public string ConnectionId { get; }
}

/// <summary>
/// Context for a session spawned event.
/// </summary>
public class SessionContext : BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the SessionContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="sessionId">Unique identifier for the session.</param>
    /// <param name="toolName">Name of the tool the session handles.</param>
    /// <param name="referenceId">Reference ID for session pooling, if applicable.</param>
    public SessionContext(string correlationId, string sessionId, string toolName, string? referenceId = null, string? prompt = null)
        : base(correlationId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        ReferenceId = referenceId;
        Prompt = prompt;
    }

    /// <summary>
    /// Gets the unique identifier for the session.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the name of the tool the session handles.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the reference ID for session pooling, if applicable.
    /// </summary>
    public string? ReferenceId { get; }

    /// <summary>
    /// Gets the resolved system prompt that was sent to the agent when this session was spawned.
    /// Null for lifecycle events that do not involve a new agent (pool wake, drop, etc.).
    /// </summary>
    public string? Prompt { get; }
}

/// <summary>
/// Context for a session dropped event.
/// </summary>
public class SessionDroppedContext : SessionContext
{
    /// <summary>
    /// Initializes a new instance of the SessionDroppedContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="sessionId">Unique identifier for the session.</param>
    /// <param name="toolName">Name of the tool the session handles.</param>
    /// <param name="reason">Reason the session was dropped.</param>
    /// <param name="referenceId">Reference ID for session pooling, if applicable.</param>
    public SessionDroppedContext(string correlationId, string sessionId, string toolName, SessionDropReason reason, string? referenceId = null)
        : base(correlationId, sessionId, toolName, referenceId)
    {
        Reason = reason;
    }

    /// <summary>
    /// Gets the reason the session was dropped.
    /// </summary>
    public SessionDropReason Reason { get; }
}

/// <summary>
/// Context for an agent crashed event.
/// </summary>
public class AgentCrashedContext : SessionContext
{
    /// <summary>
    /// Initializes a new instance of the AgentCrashedContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="sessionId">Unique identifier for the session.</param>
    /// <param name="toolName">Name of the tool the session handles.</param>
    /// <param name="exception">The exception that caused the crash.</param>
    /// <param name="referenceId">Reference ID for session pooling, if applicable.</param>
    public AgentCrashedContext(string correlationId, string sessionId, string toolName, Exception exception, string? referenceId = null)
        : base(correlationId, sessionId, toolName, referenceId)
    {
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    /// <summary>
    /// Gets the exception that caused the agent to crash.
    /// </summary>
    public Exception Exception { get; }
}

/// <summary>
/// Context for a response delivered event.
/// </summary>
public class ResponseContext : BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the ResponseContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="sessionId">Unique identifier for the session.</param>
    /// <param name="toolName">Name of the tool the response is for.</param>
    /// <param name="message">The response message.</param>
    /// <param name="durationMs">Duration of processing in milliseconds.</param>
    /// <param name="metadata">Optional metadata about the response.</param>
    public ResponseContext(string correlationId, string sessionId, string toolName, string message, long durationMs, Dictionary<string, object?>? metadata = null)
        : base(correlationId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        DurationMs = durationMs;
        Metadata = metadata ?? new();
    }

    /// <summary>
    /// Gets the unique identifier for the session.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the response message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the duration of processing in milliseconds.
    /// </summary>
    public long DurationMs { get; }

    /// <summary>
    /// Gets optional metadata about the response.
    /// </summary>
    public Dictionary<string, object?> Metadata { get; }
}

/// <summary>
/// Context for an input requested event.
/// </summary>
public class InputRequestContext : BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the InputRequestContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="sessionId">Unique identifier for the session.</param>
    /// <param name="toolName">Name of the tool requesting input.</param>
    /// <param name="question">The question being asked.</param>
    /// <param name="options">Optional list of valid options.</param>
    public InputRequestContext(string correlationId, string sessionId, string toolName, string question, List<string>? options = null)
        : base(correlationId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Question = question ?? throw new ArgumentNullException(nameof(question));
        Options = options ?? new();
    }

    /// <summary>
    /// Gets the unique identifier for the session.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the name of the tool requesting input.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the question being asked.
    /// </summary>
    public string Question { get; }

    /// <summary>
    /// Gets the optional list of valid options.
    /// </summary>
    public List<string> Options { get; }
}

/// <summary>
/// Context for an input received event.
/// </summary>
public class InputReceivedContext : BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the InputReceivedContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="sessionId">Unique identifier for the session.</param>
    /// <param name="toolName">Name of the tool that received input.</param>
    /// <param name="input">The input that was received.</param>
    public InputReceivedContext(string correlationId, string sessionId, string toolName, string input)
        : base(correlationId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Input = input ?? throw new ArgumentNullException(nameof(input));
    }

    /// <summary>
    /// Gets the unique identifier for the session.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the name of the tool that received input.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the input that was received.
    /// </summary>
    public string Input { get; }
}

/// <summary>
/// Context for a caller disconnected event.
/// </summary>
public class CallerDisconnectedContext : BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the CallerDisconnectedContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="connectionId">ID of the connection that disconnected.</param>
    public CallerDisconnectedContext(string correlationId, string connectionId)
        : base(correlationId)
    {
        ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
    }

    /// <summary>
    /// Gets the connection ID that disconnected.
    /// </summary>
    public string ConnectionId { get; }
}

/// <summary>
/// Context for a configuration reloaded event.
/// </summary>
public class ConfigReloadedContext : BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the ConfigReloadedContext class.
    /// </summary>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="previousConfig">The previous configuration.</param>
    /// <param name="newConfig">The new configuration.</param>
    public ConfigReloadedContext(string correlationId, BridgeConfiguration previousConfig, BridgeConfiguration newConfig)
        : base(correlationId)
    {
        PreviousConfig = previousConfig ?? throw new ArgumentNullException(nameof(previousConfig));
        NewConfig = newConfig ?? throw new ArgumentNullException(nameof(newConfig));
    }

    /// <summary>
    /// Gets the previous configuration.
    /// </summary>
    public BridgeConfiguration PreviousConfig { get; }

    /// <summary>
    /// Gets the new configuration.
    /// </summary>
    public BridgeConfiguration NewConfig { get; }
}
