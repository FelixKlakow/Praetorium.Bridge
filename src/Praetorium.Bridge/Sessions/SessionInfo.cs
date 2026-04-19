using System;

namespace Praetorium.Bridge.Sessions;

/// <summary>
/// Information about a session including its state and metadata.
/// </summary>
public class SessionInfo
{
    /// <summary>
    /// Initializes a new instance of the SessionInfo class.
    /// </summary>
    /// <param name="sessionId">Unique identifier for the session.</param>
    /// <param name="toolName">Name of the tool the session handles.</param>
    /// <param name="state">Current state of the session.</param>
    /// <param name="createdAt">Timestamp when the session was created.</param>
    /// <param name="referenceId">Optional reference ID for session pooling.</param>
    /// <param name="connectionId">Optional connection ID for tracking the client.</param>
    /// <param name="model">Optional model name being used by the agent.</param>
    public SessionInfo(
        string sessionId,
        string toolName,
        SessionState state,
        DateTimeOffset createdAt,
        string? referenceId = null,
        string? connectionId = null,
        string? model = null)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        State = state;
        CreatedAt = createdAt;
        LastActivityAt = createdAt;
        ReferenceId = referenceId;
        ConnectionId = connectionId;
        Model = model;
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
    /// Gets or sets the optional reference ID for session pooling.
    /// </summary>
    public string? ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the optional connection ID for tracking the client.
    /// </summary>
    public string? ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the current state of the session.
    /// </summary>
    public SessionState State { get; set; }

    /// <summary>
    /// Gets the timestamp when the session was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets or sets the timestamp of the last activity in the session.
    /// </summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>
    /// Gets or sets the optional model name being used by the agent.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the optional current question when in WaitingInput state.
    /// </summary>
    public string? CurrentQuestion { get; set; }

    /// <summary>
    /// Gets or sets the count of tool calls made in this session.
    /// </summary>
    public int ToolCallCount { get; set; }

    /// <summary>
    /// Gets the duration of the session in milliseconds since creation.
    /// </summary>
    public long DurationMs => (long)(DateTimeOffset.UtcNow - CreatedAt).TotalMilliseconds;
}
