using System;
using System.Collections.Generic;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Signaling;

namespace Praetorium.Bridge.Agents;

/// <summary>
/// Context information for spawning a new agent session.
/// </summary>
public class AgentContext
{
    /// <summary>
    /// Initializes a new instance of the AgentContext class.
    /// </summary>
    /// <param name="toolName">The name of the tool the agent is being spawned to handle.</param>
    /// <param name="prompt">The system prompt for the agent.</param>
    /// <param name="agentConfiguration">The agent configuration.</param>
    /// <param name="toolSources">The MCP tool sources to connect to.</param>
    /// <param name="signalingTools">The signaling tool definitions available to the agent.</param>
    public AgentContext(
        string toolName,
        string prompt,
        AgentConfiguration agentConfiguration,
        List<AgentToolSource> toolSources,
        List<SignalingToolDefinition> signalingTools)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        AgentConfiguration = agentConfiguration ?? throw new ArgumentNullException(nameof(agentConfiguration));
        ToolSources = toolSources ?? throw new ArgumentNullException(nameof(toolSources));
        SignalingTools = signalingTools ?? throw new ArgumentNullException(nameof(signalingTools));
    }

    /// <summary>
    /// Gets the name of the tool the agent is handling.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Gets the system prompt for the agent.
    /// </summary>
    public string Prompt { get; }

    /// <summary>
    /// Gets the agent configuration.
    /// </summary>
    public AgentConfiguration AgentConfiguration { get; }

    /// <summary>
    /// Gets the MCP tool sources to connect to.
    /// </summary>
    public List<AgentToolSource> ToolSources { get; }

    /// <summary>
    /// Gets the signaling tool definitions available to the agent.
    /// </summary>
    public List<SignalingToolDefinition> SignalingTools { get; }
}
