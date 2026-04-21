using System.Collections.Generic;
using System.Text.Json;
using Praetorium.Bridge.Prompts;
using Xunit;

namespace Praetorium.Bridge.Tests.Prompts;

/// <summary>
/// Covers the placeholder substitution engine used by <see cref="PromptResolver"/>
/// and the signaling markdown renderer. Production prompt files use
/// <c>{{camelCase}}</c> placeholders \u2014 see <c>code-reviewer.md</c> for an example.
/// Tests also cover legacy <c>UPPER_SNAKE_CASE</c> placeholders and conditional /
/// iteration blocks.
/// </summary>
public class PlaceholderEngineTests
{
    private static JsonElement JE(object? value) =>
        JsonSerializer.SerializeToElement(value);

    [Fact]
    public void Render_CamelCasePlaceholder_IsReplacedWithDictionaryValue()
    {
        var template = "workspace: {{workspace}}";
        var parameters = new Dictionary<string, JsonElement> { ["workspace"] = JE("C:\\repo") };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("workspace: C:\\repo", result);
    }

    [Fact]
    public void Render_CamelCaseIfBlock_IncludesContentWhenValuePresent()
    {
        var template = "prefix {{#if baseBranch}}base={{baseBranch}}{{/if}} suffix";
        var parameters = new Dictionary<string, JsonElement> { ["baseBranch"] = JE("main") };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("prefix base=main suffix", result);
    }

    [Fact]
    public void Render_CamelCaseIfBlock_RemovesContentWhenValueMissing()
    {
        var template = "prefix {{#if baseBranch}}base={{baseBranch}}{{/if}}suffix";
        var parameters = new Dictionary<string, JsonElement>();

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("prefix suffix", result);
    }

    [Fact]
    public void Render_CamelCaseIfBlock_RemovesContentWhenValueIsEmptyString()
    {
        var template = "{{#if baseBranch}}present{{/if}}";
        var parameters = new Dictionary<string, JsonElement> { ["baseBranch"] = JE(string.Empty) };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Render_CamelCaseEachBlock_IteratesArrayItems()
    {
        var template = "{{#each focusAreas}}- {{.}}\n{{/each}}";
        var parameters = new Dictionary<string, JsonElement> { ["focusAreas"] = JE(new[] { "security", "perf" }) };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("- security\n- perf\n", result);
    }

    [Fact]
    public void Render_UpperSnakeCasePlaceholder_MapsToCamelCaseParameter()
    {
        var template = "Branch: {{BRANCH}}, Focus areas:\n{{#each FOCUS_AREAS}}- {{.}}\n{{/each}}";
        var parameters = new Dictionary<string, JsonElement>
        {
            ["branch"] = JE("feature/x"),
            ["focusAreas"] = JE(new[] { "security", "perf" }),
        };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("Branch: feature/x, Focus areas:\n- security\n- perf\n", result);
    }

    [Fact]
    public void Render_UpperSnakeCaseIfBlock_WorksForBackwardCompatibility()
    {
        var template = "{{#if FOCUS_AREAS}}has areas{{/if}}";
        var parameters = new Dictionary<string, JsonElement> { ["focusAreas"] = JE(new[] { "a" }) };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("has areas", result);
    }

    [Fact]
    public void Render_RealCodeReviewerTemplate_ProducesRenderedMarkdown()
    {
        // Exact excerpt from prompts/code-reviewer.md - the bug this fixes.
        var template =
            "- **Workspace:** `{{workspace}}`\n" +
            "{{#if baseBranch}}- **Base branch / ref:** `{{baseBranch}}`\n{{/if}}";
        var parameters = new Dictionary<string, JsonElement>
        {
            ["workspace"] = JE("C:\\repo"),
            ["baseBranch"] = JE("main"),
        };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("- **Workspace:** `C:\\repo`\n- **Base branch / ref:** `main`\n", result);
    }

    [Fact]
    public void Render_EnvVariablePlaceholder_ReadsFromEnvironment()
    {
        const string varName = "PRAETORIUM_TEST_VAR_ABC";
        System.Environment.SetEnvironmentVariable(varName, "env-value");
        try
        {
            var template = "Env: {{ENV:PRAETORIUM_TEST_VAR_ABC}}";
            var result = PlaceholderEngine.Render(template, new Dictionary<string, JsonElement>());
            Assert.Equal("Env: env-value", result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void Render_EnvVariable_UnsetReturnsEmpty()
    {
        var template = "Env: [{{ENV:PRAETORIUM_UNSET_VAR_XYZ}}]";
        var result = PlaceholderEngine.Render(template, new Dictionary<string, JsonElement>());
        Assert.Equal("Env: []", result);
    }

    [Fact]
    public void Render_MissingPlaceholder_IsReplacedWithEmptyString()
    {
        var template = "start {{unknownKey}} end";
        var result = PlaceholderEngine.Render(template, new Dictionary<string, JsonElement>());
        Assert.Equal("start  end", result);
    }

    [Fact]
    public void Render_NullTemplate_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(() =>
            PlaceholderEngine.Render(null!, new Dictionary<string, JsonElement>()));
    }

    [Fact]
    public void Render_NullParameters_TreatedAsEmpty()
    {
        // The engine must tolerate null parameters for callers that pass them
        // unchecked from upstream code.
        var result = PlaceholderEngine.Render("hello {{name}}", null!);
        Assert.Equal("hello ", result);
    }

    [Fact]
    public void Render_PlaceholderWithDigits_IsSupported()
    {
        var template = "{{var1}} / {{VAR_2}}";
        var parameters = new Dictionary<string, JsonElement>
        {
            ["var1"] = JE("one"),
            ["var2"] = JE("two"),
        };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("one / two", result);
    }

    [Fact]
    public void Render_NestedIfInsideEach_IsProcessed()
    {
        var template = "{{#each items}}- {{.}}{{#if suffix}} ({{suffix}}){{/if}}\n{{/each}}";
        var parameters = new Dictionary<string, JsonElement>
        {
            ["items"] = JE(new[] { "a", "b" }),
            ["suffix"] = JE("end"),
        };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("- a (end)\n- b (end)\n", result);
    }

    [Fact]
    public void Render_BooleanParameter_True_IncludesIfBlock()
    {
        var template = "{{#if enabled}}ON{{/if}}";
        var parameters = new Dictionary<string, JsonElement> { ["enabled"] = JE(true) };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("ON", result);
    }

    [Fact]
    public void Render_BooleanParameter_False_OmitsIfBlock()
    {
        var template = "{{#if enabled}}ON{{/if}}";
        var parameters = new Dictionary<string, JsonElement> { ["enabled"] = JE(false) };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Render_NumberParameter_IsRendered()
    {
        var template = "count={{count}}";
        var parameters = new Dictionary<string, JsonElement> { ["count"] = JE(42) };

        var result = PlaceholderEngine.Render(template, parameters);

        Assert.Equal("count=42", result);
    }
}
