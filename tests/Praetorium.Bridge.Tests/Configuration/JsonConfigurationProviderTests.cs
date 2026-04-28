using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Configuration;
using Xunit;

namespace Praetorium.Bridge.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="JsonConfigurationProvider"/> focusing on the
/// hot-reload and save-notification behaviours.
/// </summary>
public class JsonConfigurationProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public JsonConfigurationProviderTests()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "praetorium-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SaveAsync_FiresOnConfigurationChanged()
    {
        // Arrange – create an initial config file so the provider can start
        await File.WriteAllTextAsync(_configPath, "{}");
        using var provider = new JsonConfigurationProvider(_configPath);

        BridgeConfiguration? receivedConfig = null;
        provider.OnConfigurationChanged += cfg => receivedConfig = cfg;

        var updated = new BridgeConfiguration
        {
            Tools = new Dictionary<string, ToolDefinition>
            {
                ["my-tool"] = new ToolDefinition { Description = "updated description" }
            }
        };

        // Act
        await provider.SaveAsync(updated, CancellationToken.None);

        // Assert – the event must have been raised with the saved configuration
        Assert.NotNull(receivedConfig);
        Assert.True(receivedConfig!.Tools.ContainsKey("my-tool"));
        Assert.Equal("updated description", receivedConfig.Tools["my-tool"].Description);
    }

    [Fact]
    public async Task SaveAsync_UpdatesInMemoryConfiguration()
    {
        await File.WriteAllTextAsync(_configPath, "{}");
        using var provider = new JsonConfigurationProvider(_configPath);

        var updated = new BridgeConfiguration
        {
            Tools = new Dictionary<string, ToolDefinition>
            {
                ["tool-a"] = new ToolDefinition { Description = "desc-a" }
            }
        };

        await provider.SaveAsync(updated, CancellationToken.None);

        Assert.True(provider.Configuration.Tools.ContainsKey("tool-a"));
        Assert.Equal("desc-a", provider.Configuration.Tools["tool-a"].Description);
    }

    [Fact]
    public async Task ReloadAsync_FiresOnConfigurationChanged()
    {
        var initial = new BridgeConfiguration
        {
            Tools = new Dictionary<string, ToolDefinition>
            {
                ["t"] = new ToolDefinition { Description = "old" }
            }
        };

        var initialJson = System.Text.Json.JsonSerializer.Serialize(initial,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, initialJson);

        using var provider = new JsonConfigurationProvider(_configPath);

        // Write a new version directly to disk
        var updated = new BridgeConfiguration
        {
            Tools = new Dictionary<string, ToolDefinition>
            {
                ["t"] = new ToolDefinition { Description = "new" }
            }
        };
        var updatedJson = System.Text.Json.JsonSerializer.Serialize(updated,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configPath, updatedJson);

        BridgeConfiguration? receivedConfig = null;
        provider.OnConfigurationChanged += cfg => receivedConfig = cfg;

        await provider.ReloadAsync(CancellationToken.None);

        Assert.NotNull(receivedConfig);
        Assert.Equal("new", receivedConfig!.Tools["t"].Description);
    }
}
