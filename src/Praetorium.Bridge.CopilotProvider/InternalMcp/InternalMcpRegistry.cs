using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Praetorium.Bridge.CopilotProvider.InternalMcp;

/// <inheritdoc />
internal sealed class InternalMcpRegistry : IInternalMcpRegistry
{
    private readonly ConcurrentDictionary<string, InternalMcpRegistryEntry> _entries = new(StringComparer.Ordinal);

    public void Register(string sessionKey, InternalMcpRegistryEntry entry)
    {
        if (string.IsNullOrEmpty(sessionKey))
            throw new ArgumentException("Session key cannot be null or empty.", nameof(sessionKey));
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (!_entries.TryAdd(sessionKey, entry))
        {
            throw new InvalidOperationException(
                $"Internal MCP session key '{sessionKey}' is already registered.");
        }
    }

    public bool Unregister(string sessionKey)
    {
        if (string.IsNullOrEmpty(sessionKey))
            return false;
        return _entries.TryRemove(sessionKey, out _);
    }

    public bool TryGet(string sessionKey, [MaybeNullWhen(false)] out InternalMcpRegistryEntry entry)
    {
        if (string.IsNullOrEmpty(sessionKey))
        {
            entry = null;
            return false;
        }
        return _entries.TryGetValue(sessionKey, out entry);
    }
}
