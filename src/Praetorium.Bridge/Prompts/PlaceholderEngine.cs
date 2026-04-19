using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Praetorium.Bridge.Prompts;

/// <summary>
/// Static engine for rendering templates with placeholder substitution, conditionals, and iteration.
/// </summary>
public static class PlaceholderEngine
{
    /// <summary>
    /// Renders a template by substituting placeholders with values from the parameters dictionary.
    /// Supports: {{NAME}} for simple substitution, {{#if NAME}}...{{/if}} for conditionals,
    /// {{#each NAME}}...{{/each}} for iteration, and {{ENV:VAR_NAME}} for environment variables.
    /// </summary>
    /// <param name="template">The template string containing placeholders.</param>
    /// <param name="parameters">Dictionary of parameter names (in camelCase) to JSON values.</param>
    /// <returns>The rendered template with placeholders replaced.</returns>
    public static string Render(string template, Dictionary<string, JsonElement> parameters)
    {
        if (template == null)
            throw new ArgumentNullException(nameof(template));

        if (parameters == null)
            parameters = new Dictionary<string, JsonElement>();

        // Process the template recursively to handle nested constructs
        return ProcessTemplate(template, parameters);
    }

    /// <summary>
    /// Converts a camelCase parameter name to UPPER_SNAKE_CASE placeholder format.
    /// </summary>
    /// <remarks>
    /// Examples:
    /// - focusAreas -> FOCUS_AREAS
    /// - branch -> BRANCH
    /// - reviewType -> REVIEW_TYPE
    /// </remarks>
    private static string ConvertToPlaceholderName(string camelCaseName)
    {
        if (string.IsNullOrEmpty(camelCaseName))
            return camelCaseName;

        var sb = new StringBuilder();
        for (int i = 0; i < camelCaseName.Length; i++)
        {
            char c = camelCaseName[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private static string ProcessTemplate(string template, Dictionary<string, JsonElement> parameters)
    {
        // Process #each blocks first (innermost to outermost)
        template = ProcessEachBlocks(template, parameters);

        // Process #if blocks
        template = ProcessIfBlocks(template, parameters);

        // Process simple substitutions
        template = ProcessSimpleSubstitutions(template, parameters);

        return template;
    }

    private static string ProcessSimpleSubstitutions(string template, Dictionary<string, JsonElement> parameters)
    {
        // Pattern: {{NAME}} or {{ENV:VAR_NAME}}
        var pattern = @"\{\{(?:ENV:([A-Z_0-9]+)|([A-Z_0-9]+))\}\}";
        return Regex.Replace(template, pattern, match =>
        {
            string envVar = match.Groups[1].Value;
            string varName = match.Groups[2].Value;

            if (!string.IsNullOrEmpty(envVar))
            {
                // Handle environment variables
                return Environment.GetEnvironmentVariable(envVar) ?? string.Empty;
            }

            // Find matching parameter by converting from UPPER_SNAKE_CASE back to camelCase
            var camelCaseParam = ConvertFromPlaceholder(varName);
            if (parameters.TryGetValue(camelCaseParam, out var value))
            {
                return JsonElementToString(value);
            }

            // If not found, remove the placeholder
            return string.Empty;
        });
    }

    private static string ProcessIfBlocks(string template, Dictionary<string, JsonElement> parameters)
    {
        // Pattern: {{#if NAME}}...{{/if}}
        var pattern = @"\{\{#if\s+([A-Z_]+)\}\}(.*?)\{\{/if\}\}";
        return Regex.Replace(template, pattern, match =>
        {
            string varName = match.Groups[1].Value;
            string content = match.Groups[2].Value;

            var camelCaseParam = ConvertFromPlaceholder(varName);
            if (parameters.TryGetValue(camelCaseParam, out var value))
            {
                if (HasValue(value))
                {
                    // Recursively process nested content
                    return ProcessTemplate(content, parameters);
                }
            }

            // Condition is false or parameter not found, remove the block
            return string.Empty;
        }, RegexOptions.Singleline);
    }

    private static string ProcessEachBlocks(string template, Dictionary<string, JsonElement> parameters)
    {
        // Pattern: {{#each NAME}}...{{/each}}
        var pattern = @"\{\{#each\s+([A-Z_]+)\}\}(.*?)\{\{/each\}\}";
        return Regex.Replace(template, pattern, match =>
        {
            string varName = match.Groups[1].Value;
            string content = match.Groups[2].Value;

            var camelCaseParam = ConvertFromPlaceholder(varName);
            if (parameters.TryGetValue(camelCaseParam, out var arrayValue))
            {
                if (arrayValue.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var item in arrayValue.EnumerateArray())
                    {
                        // For each iteration, replace {{.}} with current item
                        var itemContent = content.Replace("{{.}}", JsonElementToString(item));
                        // Recursively process nested placeholders
                        itemContent = ProcessTemplate(itemContent, parameters);
                        sb.Append(itemContent);
                    }
                    return sb.ToString();
                }
            }

            // Array not found or not an array, remove the block
            return string.Empty;
        }, RegexOptions.Singleline);
    }

    private static bool HasValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => false,
            JsonValueKind.False => false,
            JsonValueKind.String => !string.IsNullOrEmpty(element.GetString()),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.Object => true,
            _ => true
        };
    }

    private static string JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Array => string.Empty,
            JsonValueKind.Object => element.GetRawText(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Converts an UPPER_SNAKE_CASE placeholder name back to camelCase for parameter lookup.
    /// </summary>
    private static string ConvertFromPlaceholder(string upperSnakeCase)
    {
        if (string.IsNullOrEmpty(upperSnakeCase))
            return upperSnakeCase;

        var parts = upperSnakeCase.Split('_');
        var sb = new StringBuilder();

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0)
                continue;

            if (i == 0)
            {
                // First part is all lowercase
                sb.Append(part.ToLowerInvariant());
            }
            else
            {
                // Subsequent parts: capitalize first letter, rest lowercase
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1)
                {
                    sb.Append(part.Substring(1).ToLowerInvariant());
                }
            }
        }

        return sb.ToString();
    }
}
