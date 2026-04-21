using System;
using System.Collections.Generic;
using System.Text.Json;
using Praetorium.Bridge.Configuration;

namespace Praetorium.Bridge.Tools;

/// <summary>
/// Binds and validates tool parameters from incoming MCP calls.
/// </summary>
public class ToolParameterBinder
{
    /// <summary>
    /// Binds parameters for a tool call, extracting reserved parameters and merging fixed parameters.
    /// </summary>
    /// <param name="toolDefinition">The tool definition containing parameter metadata.</param>
    /// <param name="arguments">The JSON arguments provided in the MCP call.</param>
    /// <returns>A ToolCallContext containing bound parameters and control signals.</returns>
    /// <exception cref="ArgumentException">Thrown when required parameters are missing or validation fails.</exception>
    public ToolCallContext Bind(ToolDefinition toolDefinition, JsonElement arguments)
    {
        if (toolDefinition == null)
            throw new ArgumentNullException(nameof(toolDefinition));

        // Extract reserved parameters from arguments
        bool resetSession = false;
        string? input = null;
        string? referenceId = null;

        // The configured parameter name that carries the reference ID (user-defined, e.g. "referenceId")
        var referenceIdParamName = toolDefinition.Session?.ReferenceIdParameter;

        if (arguments.ValueKind == JsonValueKind.Object)
        {
            if (arguments.TryGetProperty(ReservedParameters.ResetSession, out var resetProp))
            {
                resetSession = resetProp.ValueKind == JsonValueKind.True;
            }

            if (arguments.TryGetProperty(ReservedParameters.Input, out var inputProp))
            {
                input = inputProp.GetString();
            }

            if (arguments.TryGetProperty(ReservedParameters.ReferenceId, out var refIdProp))
            {
                referenceId = refIdProp.GetString();
            }
            // Also accept the user-configured parameter name as the reference ID
            else if (!string.IsNullOrEmpty(referenceIdParamName)
                     && arguments.TryGetProperty(referenceIdParamName, out var namedRefIdProp))
            {
                referenceId = namedRefIdProp.GetString();
            }
        }

        // Build the bound parameters dictionary, excluding reserved parameters
        var boundParameters = new Dictionary<string, JsonElement>();

        // Start with fixed parameters from the configuration
        if (toolDefinition.FixedParameters != null)
        {
            foreach (var kvp in toolDefinition.FixedParameters)
            {
                boundParameters[kvp.Key] = kvp.Value;
            }
        }

        // Add regular parameters from arguments
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in arguments.EnumerateObject())
            {
                // Skip reserved parameters
                if (ReservedParameters.IsReserved(property.Name))
                    continue;

                // Skip the configured reference ID parameter — it is used for session routing only
                // and must not be forwarded to the agent
                if (!string.IsNullOrEmpty(referenceIdParamName) && property.Name == referenceIdParamName)
                    continue;

                // Regular parameters override fixed parameters
                boundParameters[property.Name] = property.Value;
            }
        }

        // Validate required parameters
        if (toolDefinition.Parameters != null)
        {
            foreach (var paramDef in toolDefinition.Parameters)
            {
                // The referenceIdParameter is stripped from boundParameters intentionally
                // (used for session routing only), so skip it during required-param validation.
                if (!string.IsNullOrEmpty(referenceIdParamName) && paramDef.Key == referenceIdParamName)
                    continue;

                if (paramDef.Value.Required && !boundParameters.ContainsKey(paramDef.Key))
                {
                    throw new ArgumentException(
                        $"Required parameter '{paramDef.Key}' is missing.",
                        nameof(arguments));
                }
            }
        }

        return new ToolCallContext(boundParameters, resetSession, input, referenceId, null);
    }
}
