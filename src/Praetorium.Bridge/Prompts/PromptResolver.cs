using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Configuration;

namespace Praetorium.Bridge.Prompts;

/// <summary>
/// Resolves prompt templates by reading files and applying placeholder substitution.
/// </summary>
public class PromptResolver : IPromptResolver
{
    private readonly string _basePath;
    private readonly Dictionary<string, string> _cache = new();

    /// <summary>
    /// Initializes a new instance of the PromptResolver class.
    /// </summary>
    /// <param name="basePath">The base path relative to which prompt files are resolved (typically the config file directory).</param>
    public PromptResolver(string basePath)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
    }

    /// <summary>
    /// Resolves a prompt template by reading the file and substituting parameters.
    /// </summary>
    public async Task<string> ResolveAsync(
        string toolName,
        ToolDefinition toolDef,
        Dictionary<string, JsonElement> parameters,
        CancellationToken ct)
    {
        if (toolName == null)
            throw new ArgumentNullException(nameof(toolName));

        if (toolDef == null)
            throw new ArgumentNullException(nameof(toolDef));

        if (parameters == null)
            parameters = new Dictionary<string, JsonElement>();

        // Get agent configuration
        var agentConfig = toolDef.Agent ?? throw new InvalidOperationException(
            $"No agent configuration found for tool '{toolName}'. Please configure an agent.");

        // Determine prompt file path
        var promptFile = agentConfig.PromptFile;
        if (string.IsNullOrEmpty(promptFile))
        {
            throw new InvalidOperationException(
                $"No prompt file configured for tool '{toolName}'.");
        }

        var fullPath = Path.Combine(_basePath, promptFile);
        fullPath = Path.GetFullPath(fullPath);

        // Read prompt file (with simple caching)
        string promptContent;
        if (_cache.TryGetValue(fullPath, out var cached))
        {
            promptContent = cached;
        }
        else
        {
            try
            {
                promptContent = await File.ReadAllTextAsync(fullPath, ct);
                _cache[fullPath] = promptContent;
            }
            catch (FileNotFoundException)
            {
                throw new InvalidOperationException(
                    $"Prompt file not found: {fullPath}");
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    $"Error reading prompt file {fullPath}: {ex.Message}");
            }
        }

        // Apply placeholder substitution
        var resolved = PlaceholderEngine.Render(promptContent, parameters);

        return resolved;
    }
}
