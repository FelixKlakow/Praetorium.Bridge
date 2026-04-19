using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Configuration;
using BridgeConfigurationProvider = Praetorium.Bridge.Configuration.IConfigurationProvider;

namespace Praetorium.Bridge.Web.Services;

/// <summary>
/// Service for managing configuration through the UI, wrapping the IConfigurationProvider.
/// </summary>
public class ConfigurationService
{
    private const string PromptsDirectoryName = "prompts";
    private const string PromptFileSearchPattern = "*.md";
    private static readonly Regex PromptFileNameRegex = new(
        @"^[a-zA-Z0-9][a-zA-Z0-9_\-./ ]*\.md$",
        RegexOptions.Compiled);

    private readonly BridgeConfigurationProvider _provider;

    /// <summary>
    /// Initializes a new instance of the ConfigurationService class.
    /// </summary>
    public ConfigurationService(BridgeConfigurationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Gets the current bridge configuration.
    /// </summary>
    public BridgeConfiguration GetConfiguration()
    {
        return _provider.Configuration;
    }

    /// <summary>
    /// Gets a tool definition by name.
    /// </summary>
    public ToolDefinition? GetToolDefinition(string toolName)
    {
        var config = _provider.Configuration;
        if (config?.Tools == null)
            return null;

        return config.Tools.TryGetValue(toolName, out var tool) ? tool : null;
    }

    /// <summary>
    /// Saves a tool definition.
    /// </summary>
    public async Task SaveToolDefinitionAsync(string toolName, ToolDefinition tool, CancellationToken ct = default)
    {
        var config = _provider.Configuration;
        if (config == null || config.Tools == null)
            throw new InvalidOperationException("Configuration is not available.");

        config.Tools[toolName] = tool ?? throw new ArgumentNullException(nameof(tool));
        await _provider.SaveAsync(config, ct);
    }

    /// <summary>
    /// Deletes a tool definition.
    /// </summary>
    public async Task DeleteToolDefinitionAsync(string toolName, CancellationToken ct = default)
    {
        var config = _provider.Configuration;
        if (config == null || config.Tools == null)
            throw new InvalidOperationException("Configuration is not available.");

        config.Tools.Remove(toolName);
        await _provider.SaveAsync(config, ct);
    }

    /// <summary>
    /// Saves an agent tool source entry, adding it if it does not yet exist.
    /// </summary>
    public async Task SaveAgentToolSourceAsync(string name, AgentToolSource source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Agent source name is required.", nameof(name));

        var config = _provider.Configuration;
        if (config == null)
            throw new InvalidOperationException("Configuration is not available.");

        config.AgentToolSources ??= new Dictionary<string, AgentToolSource>();
        config.AgentToolSources[name] = source ?? throw new ArgumentNullException(nameof(source));
        await _provider.SaveAsync(config, ct);
    }

    /// <summary>
    /// Deletes an agent tool source entry.
    /// </summary>
    public async Task DeleteAgentToolSourceAsync(string name, CancellationToken ct = default)
    {
        var config = _provider.Configuration;
        if (config?.AgentToolSources == null)
            throw new InvalidOperationException("Configuration is not available.");

        config.AgentToolSources.Remove(name);
        await _provider.SaveAsync(config, ct);
    }

    /// <summary>
    /// Saves the defaults section of the configuration.
    /// </summary>
    public async Task SaveDefaultsAsync(DefaultsConfiguration defaults, CancellationToken ct = default)
    {
        if (defaults == null)
            throw new ArgumentNullException(nameof(defaults));

        var config = _provider.Configuration;
        if (config == null)
            throw new InvalidOperationException("Configuration is not available.");

        config.Defaults = defaults;
        await _provider.SaveAsync(config, ct);
    }

    /// <summary>
    /// Lists all prompt files that exist in the prompts directory.
    /// </summary>
    public IReadOnlyList<string> ListPromptFiles()
    {
        var promptsDir = GetPromptsDirectory();
        if (!Directory.Exists(promptsDir))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(promptsDir, PromptFileSearchPattern, SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns true when the prompt file exists on disk.
    /// </summary>
    public bool PromptFileExists(string promptFile)
    {
        ValidatePromptFileName(promptFile);
        return File.Exists(GetPromptPath(promptFile));
    }

    /// <summary>
    /// Gets the content of a prompt file. Returns an empty string when the file does not yet exist.
    /// </summary>
    public async Task<string> GetPromptContentAsync(string promptFile, CancellationToken ct = default)
    {
        ValidatePromptFileName(promptFile);
        var promptPath = GetPromptPath(promptFile);

        if (!File.Exists(promptPath))
            return string.Empty;

        return await File.ReadAllTextAsync(promptPath, ct);
    }

    /// <summary>
    /// Saves the content of a prompt file, creating it if it does not exist.
    /// </summary>
    public async Task SavePromptContentAsync(string promptFile, string content, CancellationToken ct = default)
    {
        ValidatePromptFileName(promptFile);

        var promptPath = GetPromptPath(promptFile);
        var directory = Path.GetDirectoryName(promptPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(promptPath, content ?? string.Empty, ct);
    }

    /// <summary>
    /// Deletes a prompt file from disk.
    /// </summary>
    public void DeletePromptFile(string promptFile)
    {
        ValidatePromptFileName(promptFile);
        var promptPath = GetPromptPath(promptFile);
        if (File.Exists(promptPath))
            File.Delete(promptPath);
    }

    /// <summary>
    /// Saves the entire bridge configuration.
    /// </summary>
    public async Task SaveConfigurationAsync(BridgeConfiguration config, CancellationToken ct = default)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        await _provider.SaveAsync(config, ct);
    }

    private string GetPromptsDirectory()
    {
        return Path.Combine(_provider.ConfigDirectory, PromptsDirectoryName);
    }

    private string GetPromptPath(string promptFile)
    {
        var promptsDir = GetPromptsDirectory();
        var trimmed = promptFile.Replace('\\', '/').TrimStart('/');
        if (trimmed.StartsWith("./", StringComparison.Ordinal))
            trimmed = trimmed.Substring(2);
        if (trimmed.StartsWith(PromptsDirectoryName + "/", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed.Substring(PromptsDirectoryName.Length + 1);

        var fullPath = Path.GetFullPath(Path.Combine(promptsDir, trimmed));
        var normalizedPromptsDir = Path.GetFullPath(promptsDir) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(normalizedPromptsDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Prompt path '{promptFile}' resolves outside the prompts directory.");

        return fullPath;
    }

    private static void ValidatePromptFileName(string promptFile)
    {
        if (string.IsNullOrWhiteSpace(promptFile))
            throw new ArgumentException("Prompt file name is required.", nameof(promptFile));

        var normalized = promptFile.Replace('\\', '/');
        if (normalized.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Prompt file name must not contain parent-directory segments.", nameof(promptFile));

        // The tail (actual file name) must end with .md and only contain safe characters.
        var tail = normalized;
        if (tail.StartsWith("./", StringComparison.Ordinal))
            tail = tail.Substring(2);
        if (!PromptFileNameRegex.IsMatch(tail))
            throw new ArgumentException(
                "Prompt file name must end in .md and contain only letters, numbers, underscores, hyphens, slashes, spaces, or dots.",
                nameof(promptFile));
    }
}
