using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Prompts;
using Xunit;

namespace Praetorium.Bridge.Tests.Prompts;

/// <summary>
/// End-to-end tests for <see cref="PromptResolver"/>: it must read a prompt file
/// relative to its configured base path, substitute placeholders via
/// <see cref="PlaceholderEngine"/>, and surface missing files as a clear error.
/// </summary>
public class PromptResolverTests : IDisposable
{
    private readonly string _tempDir;

    public PromptResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "praetorium-prompt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private static ToolDefinition BuildToolDefinition(string promptFile) =>
        new()
        {
            Description = "test",
            Parameters = new Dictionary<string, ParameterDefinition>(),
            Agent = new AgentConfiguration { Model = "m", PromptFile = promptFile },
        };

    private static JsonElement JE(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task ResolveAsync_SubstitutesCamelCasePlaceholders_FromRealStyleTemplate()
    {
        var promptFile = "prompt.md";
        var fullPath = Path.Combine(_tempDir, promptFile);
        await File.WriteAllTextAsync(fullPath,
            "- **Workspace:** `{{workspace}}`\n{{#if baseBranch}}- **Base:** `{{baseBranch}}`\n{{/if}}");

        var resolver = new PromptResolver(_tempDir);
        var parameters = new Dictionary<string, JsonElement>
        {
            ["workspace"] = JE("C:\\repo"),
            ["baseBranch"] = JE("main"),
        };

        var result = await resolver.ResolveAsync(
            "tool",
            BuildToolDefinition(promptFile),
            parameters,
            CancellationToken.None);

        Assert.Equal("- **Workspace:** `C:\\repo`\n- **Base:** `main`\n", result);
    }

    [Fact]
    public async Task ResolveAsync_MissingFile_ThrowsInvalidOperationException()
    {
        var resolver = new PromptResolver(_tempDir);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("tool", BuildToolDefinition("nonexistent.md"),
                new Dictionary<string, JsonElement>(), CancellationToken.None));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_NoPromptFile_ThrowsInvalidOperationException()
    {
        var resolver = new PromptResolver(_tempDir);
        var toolDef = new ToolDefinition
        {
            Description = "test",
            Parameters = new Dictionary<string, ParameterDefinition>(),
            Agent = new AgentConfiguration { Model = "m", PromptFile = null },
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("tool", toolDef,
                new Dictionary<string, JsonElement>(), CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_CachesFileContent_AcrossCalls_ButReRendersWithNewParameters()
    {
        var promptFile = "cached.md";
        var fullPath = Path.Combine(_tempDir, promptFile);
        await File.WriteAllTextAsync(fullPath, "Hello {{name}}!");

        var resolver = new PromptResolver(_tempDir);

        var first = await resolver.ResolveAsync(
            "tool", BuildToolDefinition(promptFile),
            new Dictionary<string, JsonElement> { ["name"] = JE("Alice") },
            CancellationToken.None);

        // Overwrite file: if cache works, the first content is reused.
        await File.WriteAllTextAsync(fullPath, "Changed {{name}}!");

        var second = await resolver.ResolveAsync(
            "tool", BuildToolDefinition(promptFile),
            new Dictionary<string, JsonElement> { ["name"] = JE("Bob") },
            CancellationToken.None);

        Assert.Equal("Hello Alice!", first);
        // Demonstrates that parameters are re-applied even though file is cached.
        Assert.Equal("Hello Bob!", second);
    }

    [Fact]
    public async Task ResolveAsync_NullParameters_TreatedAsEmpty()
    {
        var promptFile = "prompt.md";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, promptFile), "Static content");
        var resolver = new PromptResolver(_tempDir);

        var result = await resolver.ResolveAsync(
            "tool", BuildToolDefinition(promptFile), null!, CancellationToken.None);

        Assert.Equal("Static content", result);
    }
}
