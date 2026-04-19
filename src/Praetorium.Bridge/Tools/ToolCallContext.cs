using System.Collections.Generic;
using System.Text.Json;

namespace Praetorium.Bridge.Tools;

/// <summary>
/// Represents the context of a tool call, containing bound parameters and control signals.
/// </summary>
public class ToolCallContext
{
    /// <summary>
    /// Initializes a new instance of the ToolCallContext class.
    /// </summary>
    /// <param name="boundParameters">The parameters bound to this tool call.</param>
    /// <param name="resetSession">Whether the session should be reset.</param>
    /// <param name="input">Optional input data provided by the caller.</param>
    /// <param name="referenceId">Optional reference ID for PerReference session mode.</param>
    /// <param name="connectionId">Optional connection ID for PerConnection session mode.</param>
    public ToolCallContext(
        Dictionary<string, JsonElement> boundParameters,
        bool resetSession = false,
        string? input = null,
        string? referenceId = null,
        string? connectionId = null)
    {
        BoundParameters = boundParameters;
        ResetSession = resetSession;
        Input = input;
        ReferenceId = referenceId;
        ConnectionId = connectionId;
    }

    /// <summary>
    /// Gets the parameters bound to this tool call.
    /// </summary>
    public Dictionary<string, JsonElement> BoundParameters { get; }

    /// <summary>
    /// Gets a value indicating whether the session should be reset.
    /// </summary>
    public bool ResetSession { get; }

    /// <summary>
    /// Gets the optional input data provided by the caller.
    /// </summary>
    public string? Input { get; }

    /// <summary>
    /// Gets the optional reference ID for PerReference session mode.
    /// </summary>
    public string? ReferenceId { get; }

    /// <summary>
    /// Gets the optional connection ID for PerConnection session mode.
    /// </summary>
    public string? ConnectionId { get; }
}
