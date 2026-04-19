using System.Threading;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;
using Praetorium.Bridge.Agents;

namespace Praetorium.Bridge.CopilotProvider;

/// <summary>
/// Wraps a <see cref="CopilotSession"/> into the provider-agnostic <see cref="IAgentSession"/> interface.
/// </summary>
internal sealed class CopilotAgentSession : IAgentSession
{
    private readonly CopilotSession _session;

    internal CopilotAgentSession(CopilotSession session)
    {
        _session = session ?? throw new System.ArgumentNullException(nameof(session));
    }

    /// <inheritdoc/>
    public async Task<string> SendAsync(string message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(message))
            throw new System.ArgumentException("Message cannot be null or empty.", nameof(message));

        var response = await _session.SendAsync(new MessageOptions { Prompt = message }).ConfigureAwait(false);
        return response;
    }
}
