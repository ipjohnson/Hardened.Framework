namespace Hardened.Requests.Abstract.Timeouts;

/// <summary>
/// Something written on an operation, a class or an assembly that states a deadline.
/// </summary>
/// <remarks>
/// The interface rather than the attribute, so that
/// <see cref="Execution.IExecutionRequestHandlerInfo.TimeoutFrom"/> can read a declaration without
/// <c>Hardened.Requests.Abstract</c> knowing what declared it. <c>[Timeout]</c> is the one the
/// framework ships; an application with its own vocabulary for this - a <c>[Slo]</c> attribute
/// carrying a service level, say - implements this and is read the same way.
/// </remarks>
public interface IDeclaresTimeout {

    /// <summary>What this declaration says. Never null.</summary>
    TimeoutPolicy Timeout { get; }
}
