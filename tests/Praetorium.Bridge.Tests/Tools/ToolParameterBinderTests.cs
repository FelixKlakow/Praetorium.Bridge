using System;
using System.Collections.Generic;
using System.Text.Json;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Tools;
using Xunit;

namespace Praetorium.Bridge.Tests.Tools;

public class ToolParameterBinderTests
{
    private readonly ToolParameterBinder _binder = new();

    private static JsonElement ParseJson(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Bind_NullToolDefinition_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _binder.Bind(null!, ParseJson("{}")));
    }

    [Fact]
    public void Bind_MissingRequiredParameter_Throws()
    {
        var def = new ToolDefinition
        {
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["name"] = new() { Type = "string", Required = true }
            }
        };

        Assert.Throws<ArgumentException>(() => _binder.Bind(def, ParseJson("{}")));
    }

    [Fact]
    public void Bind_ReservedParameters_ExtractedAndStripped()
    {
        var def = new ToolDefinition();
        var json = ParseJson("""
            {
                "_resetSession": true,
                "_input": "hello",
                "_referenceId": "ref-123",
                "regular": "value"
            }
            """);

        var ctx = _binder.Bind(def, json);

        Assert.True(ctx.ResetSession);
        Assert.Equal("hello", ctx.Input);
        Assert.Equal("ref-123", ctx.ReferenceId);
        Assert.True(ctx.BoundParameters.ContainsKey("regular"));
        Assert.False(ctx.BoundParameters.ContainsKey("_resetSession"));
        Assert.False(ctx.BoundParameters.ContainsKey("_input"));
        Assert.False(ctx.BoundParameters.ContainsKey("_referenceId"));
    }

    [Fact]
    public void Bind_ConfiguredReferenceIdParameter_IsExtractedAndStripped()
    {
        var def = new ToolDefinition
        {
            Session = new SessionConfiguration { ReferenceIdParameter = "ticketId" }
        };
        var json = ParseJson("""{"ticketId": "T-42", "subject": "hello"}""");

        var ctx = _binder.Bind(def, json);

        Assert.Equal("T-42", ctx.ReferenceId);
        Assert.False(ctx.BoundParameters.ContainsKey("ticketId"));
        Assert.True(ctx.BoundParameters.ContainsKey("subject"));
    }

    [Fact]
    public void Bind_FixedParameters_AreIncluded_AndOverriddenByArguments()
    {
        var def = new ToolDefinition
        {
            FixedParameters = new Dictionary<string, JsonElement>
            {
                ["fixed"] = ParseJson("\"fixed-value\""),
                ["overridable"] = ParseJson("\"default\"")
            }
        };
        var json = ParseJson("""{"overridable": "override"}""");

        var ctx = _binder.Bind(def, json);

        Assert.Equal("fixed-value", ctx.BoundParameters["fixed"].GetString());
        Assert.Equal("override", ctx.BoundParameters["overridable"].GetString());
    }

    [Fact]
    public void Bind_NonObjectArguments_ReturnsEmptyContext()
    {
        var def = new ToolDefinition();
        var ctx = _binder.Bind(def, ParseJson("[]"));

        Assert.False(ctx.ResetSession);
        Assert.Null(ctx.Input);
        Assert.Null(ctx.ReferenceId);
        Assert.Empty(ctx.BoundParameters);
    }

    [Fact]
    public void Bind_ResetSessionFalseValue_IsRespected()
    {
        var def = new ToolDefinition();
        var ctx = _binder.Bind(def, ParseJson("""{"_resetSession": false}"""));
        Assert.False(ctx.ResetSession);
    }

    [Fact]
    public void Bind_PromptAndResume_RequiredOnNewTurn_Throws_WhenMissing()
    {
        var def = new ToolDefinition
        {
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["context"] = new() { Type = "string", Required = true, Kind = ParameterKind.PromptAndResume }
            }
        };

        Assert.Throws<ArgumentException>(() => _binder.Bind(def, ParseJson("{}"), TurnPhase.NewTurn));
    }

    [Fact]
    public void Bind_PromptAndResume_NotRequiredOnResume_AllowsMissing()
    {
        var def = new ToolDefinition
        {
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["context"] = new() { Type = "string", Required = true, Kind = ParameterKind.PromptAndResume }
            }
        };

        // No exception — Resume phase does not enforce PromptAndResume required-ness.
        var ctx = _binder.Bind(def, ParseJson("{}"), TurnPhase.Resume);
        Assert.Empty(ctx.BoundParameters);
    }

    [Fact]
    public void Bind_PromptAndResume_NotRequiredOnRejoin_AllowsMissing()
    {
        var def = new ToolDefinition
        {
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["context"] = new() { Type = "string", Required = true, Kind = ParameterKind.PromptAndResume }
            }
        };

        var ctx = _binder.Bind(def, ParseJson("{}"), TurnPhase.Rejoin);
        Assert.Empty(ctx.BoundParameters);
    }
}
