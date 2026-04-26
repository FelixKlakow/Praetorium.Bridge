namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Classifies a tool parameter by its role in the dispatch lifecycle.
/// The <see cref="Tools.ToolParameterBinder"/> uses this to decide whether a
/// parameter's <see cref="ParameterDefinition.Required"/> flag is enforced for
/// the current <see cref="Tools.TurnPhase"/>.
/// </summary>
public enum ParameterKind
{
    /// <summary>
    /// Supplied on a fresh turn. Used to render the tool prompt template.
    /// Required-enforcement active only when the turn phase is NewTurn.
    /// </summary>
    Prompt = 0,

    /// <summary>
    /// Supplied when resuming a session that is blocked on a blocking signaling
    /// tool (e.g. <c>request_input</c>). Required-enforcement active only when
    /// the turn phase is Resume.
    /// </summary>
    Resume = 1,

    /// <summary>
    /// Reserved, bridge-controlled parameter (reference id, connection id,
    /// reset session, input fallback). Never required of the caller by the
    /// binder — the dispatcher handles these itself.
    /// </summary>
    System = 2,
}
