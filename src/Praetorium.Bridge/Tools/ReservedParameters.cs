using System.Collections.Generic;
using System.Linq;

namespace Praetorium.Bridge.Tools;

/// <summary>
/// Contains reserved parameter names used by the bridge for control flow.
/// </summary>
public static class ReservedParameters
{
    /// <summary>
    /// Parameter name to reset the current session.
    /// </summary>
    public const string ResetSession = "_resetSession";

    /// <summary>
    /// Parameter name for providing input to a session.
    /// </summary>
    public const string Input = "_input";

    /// <summary>
    /// Parameter name for specifying a reference ID for PerReference session mode.
    /// </summary>
    public const string ReferenceId = "_referenceId";

    /// <summary>
    /// HashSet containing all reserved parameter names for quick lookup.
    /// </summary>
    private static readonly HashSet<string> _all = new()
    {
        ResetSession,
        Input,
        ReferenceId
    };

    /// <summary>
    /// Gets a read-only collection of all reserved parameter names.
    /// </summary>
    public static IReadOnlyCollection<string> All => _all;

    /// <summary>
    /// Determines whether the given parameter name is reserved.
    /// </summary>
    /// <param name="name">The parameter name to check.</param>
    /// <returns>True if the parameter name is reserved; otherwise, false.</returns>
    public static bool IsReserved(string name)
    {
        return _all.Contains(name);
    }
}
