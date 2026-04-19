using System.Collections.Generic;

namespace Praetorium.Bridge.Prompts;

/// <summary>
/// Result of prompt template validation.
/// </summary>
public class PromptValidationResult
{
    /// <summary>
    /// Initializes a new instance of the PromptValidationResult class.
    /// </summary>
    /// <param name="isValid">Whether the prompt template is valid.</param>
    /// <param name="warnings">Optional list of warnings (e.g., unknown placeholders).</param>
    /// <param name="unusedParameters">Optional list of parameters not used in the template.</param>
    public PromptValidationResult(
        bool isValid,
        List<string>? warnings = null,
        List<string>? unusedParameters = null)
    {
        IsValid = isValid;
        Warnings = warnings ?? new List<string>();
        UnusedParameters = unusedParameters ?? new List<string>();
    }

    /// <summary>
    /// Gets a value indicating whether the prompt template is valid.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets a list of warnings encountered during validation (e.g., unknown placeholders).
    /// </summary>
    public List<string> Warnings { get; }

    /// <summary>
    /// Gets a list of parameters that are not used in the prompt template.
    /// </summary>
    public List<string> UnusedParameters { get; }
}
