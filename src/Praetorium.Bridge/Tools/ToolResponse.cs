using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Tools;

/// <summary>
/// Represents the response sent back to external callers from a tool invocation.
/// </summary>
public class ToolResponse
{
    /// <summary>
    /// Initializes a new instance of the ToolResponse class.
    /// </summary>
    public ToolResponse()
    {
    }

    /// <summary>
    /// Gets or sets the status of the tool response.
    /// Valid values: "complete", "input_requested", "error".
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "complete";

    /// <summary>
    /// Gets or sets an optional message providing additional context about the response.
    /// </summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets optional metadata associated with the response.
    /// </summary>
    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the question to display to the user when status is "input_requested".
    /// </summary>
    [JsonPropertyName("question")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Question { get; set; }

    /// <summary>
    /// Gets or sets the list of options for user selection when status is "input_requested".
    /// </summary>
    [JsonPropertyName("options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Options { get; set; }

    /// <summary>
    /// Gets or sets the error message when status is "error".
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a ToolResponse indicating successful completion.
    /// </summary>
    /// <param name="message">Optional message describing the result.</param>
    /// <param name="metadata">Optional metadata to include with the response.</param>
    /// <returns>A ToolResponse with status "complete".</returns>
    public static ToolResponse Complete(string? message = null, object? metadata = null)
    {
        return new ToolResponse
        {
            Status = "complete",
            Message = message,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Creates a ToolResponse requesting input from the user.
    /// </summary>
    /// <param name="question">The question to ask the user.</param>
    /// <param name="options">Optional list of predefined options for the user to choose from.</param>
    /// <returns>A ToolResponse with status "input_requested".</returns>
    public static ToolResponse InputRequested(string question, List<string>? options = null)
    {
        return new ToolResponse
        {
            Status = "input_requested",
            Question = question,
            Options = options
        };
    }

    /// <summary>
    /// Creates a ToolResponse indicating an error occurred.
    /// </summary>
    /// <param name="error">A description of the error that occurred.</param>
    /// <returns>A ToolResponse with status "error".</returns>
    public static ToolResponse Error(string error)
    {
        return new ToolResponse
        {
            Status = "error",
            ErrorMessage = error
        };
    }
}
