using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Configuration;

namespace Praetorium.Bridge.Prompts;

/// <summary>
/// Interface for resolving prompt templates with parameter substitution.
/// </summary>
public interface IPromptResolver
{
    /// <summary>
    /// Resolves a prompt template by substituting placeholders with parameter values.
    /// </summary>
    /// <param name="toolName">The name of the tool for which the prompt is being resolved.</param>
    /// <param name="toolDef">The tool definition containing prompt configuration.</param>
    /// <param name="parameters">The parameters to substitute into the prompt template.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved prompt string with all placeholders replaced.</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the prompt file cannot be found or read.</exception>
    Task<string> ResolveAsync(string toolName, ToolDefinition toolDef, Dictionary<string, JsonElement> parameters, CancellationToken ct);
}
