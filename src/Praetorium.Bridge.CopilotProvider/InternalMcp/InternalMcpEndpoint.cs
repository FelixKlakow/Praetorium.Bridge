using System;

namespace Praetorium.Bridge.CopilotProvider.InternalMcp;

/// <summary>
/// Immutable description of the loopback-only internal MCP endpoint. A single
/// instance is registered as a singleton once the web host has chosen a free
/// port; <see cref="CopilotAgentProvider"/> consumes it to build the URL passed
/// to the Copilot SDK via <c>SessionConfig.McpServers</c>.
/// </summary>
public sealed class InternalMcpEndpoint
{
    /// <summary>The path under which the internal MCP server is mapped.</summary>
    public const string Path = "/mcp-int";

    /// <summary>The header carrying the opaque session key.</summary>
    public const string SessionHeaderName = "X-Praetorium-Session";

    /// <summary>The header carrying the per-session bearer token.</summary>
    public const string BearerTokenHeaderName = "X-Praetorium-Token";

    /// <summary>Initializes a new <see cref="InternalMcpEndpoint"/>.</summary>
    public InternalMcpEndpoint(int port)
    {
        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be in [1, 65535].");
        Port = port;
        Url = $"http://127.0.0.1:{port}{Path}";
    }

    /// <summary>Gets the loopback TCP port Kestrel listens on for internal MCP traffic.</summary>
    public int Port { get; }

    /// <summary>Gets the absolute URL that MCP clients connect to.</summary>
    public string Url { get; }
}
