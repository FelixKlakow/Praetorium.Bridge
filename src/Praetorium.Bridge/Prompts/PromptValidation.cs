using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Praetorium.Bridge.Configuration;

namespace Praetorium.Bridge.Prompts;

/// <summary>
/// Validates prompt templates for correctness and completeness.
/// </summary>
public static class PromptValidation
{
    /// <summary>
    /// Validates a prompt template to ensure all placeholders are defined.
    /// </summary>
    /// <param name="template">The prompt template to validate.</param>
    /// <param name="toolDef">The tool definition containing parameter specifications.</param>
    /// <returns>A PromptValidationResult indicating validity and any warnings.</returns>
    public static PromptValidationResult Validate(string template, ToolDefinition toolDef)
    {
        if (template == null)
            throw new ArgumentNullException(nameof(template));

        if (toolDef == null)
            throw new ArgumentNullException(nameof(toolDef));

        var warnings = new List<string>();
        var unusedParameters = new List<string>();

        // Extract all placeholder names from template
        var placeholdersInTemplate = ExtractPlaceholderNames(template);

        // Build set of available parameters
        var availableParams = new HashSet<string>();

        // Add parameters from tool definition
        if (toolDef.Parameters != null)
        {
            foreach (var param in toolDef.Parameters.Keys)
            {
                availableParams.Add(ConvertToPlaceholder(param));
            }
        }

        // Add fixed parameters from tool definition
        if (toolDef.FixedParameters != null)
        {
            foreach (var param in toolDef.FixedParameters.Keys)
            {
                availableParams.Add(ConvertToPlaceholder(param));
            }
        }

        // Check each placeholder in template
        foreach (var placeholder in placeholdersInTemplate)
        {
            // Skip environment variables
            if (placeholder.StartsWith("ENV:"))
                continue;

            if (!availableParams.Contains(placeholder))
            {
                warnings.Add($"Unknown placeholder: {{{{{placeholder}}}}} is not defined in parameters.");
            }
        }

        // Check for unused parameters
        foreach (var param in availableParams)
        {
            if (!placeholdersInTemplate.Contains(param))
            {
                unusedParameters.Add(param);
            }
        }

        var isValid = warnings.Count == 0;

        return new PromptValidationResult(isValid, warnings, unusedParameters);
    }

    /// <summary>
    /// Extracts all placeholder names from a template.
    /// </summary>
    private static HashSet<string> ExtractPlaceholderNames(string template)
    {
        var placeholders = new HashSet<string>();

        // Pattern for {{NAME}}, {{#if NAME}}, {{#each NAME}}, {{ENV:NAME}}
        var pattern = @"\{\{(?:#(?:if|each)\s+)?([A-Z_]+)|ENV:([A-Z_]+)\}\}";

        foreach (Match match in Regex.Matches(template, pattern))
        {
            string varName = match.Groups[1].Value;
            string envVar = match.Groups[2].Value;

            if (!string.IsNullOrEmpty(envVar))
            {
                placeholders.Add($"ENV:{envVar}");
            }
            else if (!string.IsNullOrEmpty(varName))
            {
                placeholders.Add(varName);
            }
        }

        return placeholders;
    }

    /// <summary>
    /// Converts a camelCase parameter name to UPPER_SNAKE_CASE format.
    /// </summary>
    private static string ConvertToPlaceholder(string camelCaseName)
    {
        if (string.IsNullOrEmpty(camelCaseName))
            return camelCaseName;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < camelCaseName.Length; i++)
        {
            char c = camelCaseName[i];
            if (char.IsUpper(c) && i > 0)
            {
                result.Append('_');
            }
            result.Append(char.ToUpperInvariant(c));
        }
        return result.ToString();
    }
}
