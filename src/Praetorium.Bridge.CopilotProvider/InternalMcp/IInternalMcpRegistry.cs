using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Praetorium.Bridge.Signaling;

namespace Praetorium.Bridge.CopilotProvider.InternalMcp;

/// <summary>
/// Entry in the internal MCP registry binding a loopback session key to the
/// signaling tools exposed to a specific Copilot agent session and the bearer
/// token that authorizes calls to those tools.
/// </summary>
/// <param name="Tools">The signaling tool definitions the agent may invoke.</param>
/// <param name="BearerToken">The opaque bearer token required on inbound calls.</param>
public sealed record InternalMcpRegistryEntry(
    IReadOnlyList<SignalingToolDefinition> Tools,
    string BearerToken);

/// <summary>
/// Per-process registry mapping loopback session keys to signaling tool sets.
/// The <see cref="Praetorium.Bridge.Web"/> host resolves entries when serving
/// requests on the internal MCP endpoint; <see cref="CopilotAgentProvider"/>
/// registers entries before creating a Copilot session and unregisters them
/// when the session is disposed.
/// </summary>
public interface IInternalMcpRegistry
{
    /// <summary>
    /// Registers a new session. The session key must be unique per process.
    /// </summary>
    void Register(string sessionKey, InternalMcpRegistryEntry entry);

    /// <summary>
    /// Removes a previously registered session. Returns <see langword="false"/>
    /// when no matching entry exists.
    /// </summary>
    bool Unregister(string sessionKey);

    /// <summary>
    /// Looks up the entry for a session key.
    /// </summary>
    bool TryGet(string sessionKey, [MaybeNullWhen(false)] out InternalMcpRegistryEntry entry);
}
