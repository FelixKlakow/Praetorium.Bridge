using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;

namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Represents a local-function tool definition for signaling operations.
/// </summary>
public class SignalingToolDefinition
{
    /// <summary>
    /// Initializes a new instance of the SignalingToolDefinition class.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <param name="description">A description of what the tool does.</param>
    /// <param name="parametersSchema">The JSON schema for the tool's parameters.</param>
    /// <param name="handler">The handler function that executes the tool.</param>
    public SignalingToolDefinition(
        string name,
        string description,
        JsonElement parametersSchema,
        Func<JsonElement, IProgress<ProgressNotificationValue>?, CancellationToken, Task<string>> handler)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        ParametersSchema = parametersSchema;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the description of what the tool does.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the JSON schema for the tool's parameters.
    /// </summary>
    public JsonElement ParametersSchema { get; }

    /// <summary>
    /// Gets the handler function that executes the tool. The optional
    /// <see cref="IProgress{T}"/> argument must be used by blocking handlers to emit periodic
    /// keepalive notifications while the other side is working.
    /// </summary>
    public Func<JsonElement, IProgress<ProgressNotificationValue>?, CancellationToken, Task<string>> Handler { get; }
}
