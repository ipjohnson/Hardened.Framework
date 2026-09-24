using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using ValidationModules;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Whether a handler's validation reports every failed rule, or stops at the first.
/// </summary>
/// <remarks>
/// <para>
/// Declared on the handler method, on its class, or on the <c>[HardenedModule]</c> class for every
/// handler compiled with it, and the nearest declaration wins. A handler under none reports every
/// failure, which is <see cref="ValidationStopMode.CollectAll"/>.
/// </para>
/// <para>
/// The mode belongs to the route rather than to the validated type, so one model can be checked
/// both ways on two routes. <see cref="ValidationStopMode.StopOnFirstError"/> skips the remaining
/// rules, nested members and collection elements rather than checking them and discarding what they
/// found. The refusal is the same envelope at the same status, with one entry under
/// <c>errors</c>.
/// </para>
/// <para>
/// A declaration rather than a filter. It implements <see cref="IRequestFilterProvider"/> because
/// that is what the entry-point rung collects, and it contributes no filter of its own: the
/// validation filter reads it from the handler's metadata as the chain is built.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class ValidationModeAttribute : Attribute, IRequestFilterProvider
{
    public ValidationModeAttribute(ValidationStopMode stopMode)
    {
        StopMode = stopMode;
    }

    public ValidationStopMode StopMode { get; }

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) =>
        Array.Empty<RequestFilterInfo>();

    /// <summary>
    /// The mode the nearest declaration on <paramref name="handlerInfo"/> states, or
    /// <see cref="ValidationStopMode.CollectAll"/> where nothing does.
    /// </summary>
    /// <remarks>
    /// The metadata lists the method's declarations before its class's, and carries the entry
    /// point's only where the handler declared none of its own, so the first one found is the
    /// nearest.
    /// </remarks>
    internal static ValidationStopMode For(IExecutionRequestHandlerInfo handlerInfo)
    {
        foreach (var declaration in handlerInfo.Metadata)
        {
            if (declaration is ValidationModeAttribute mode)
            {
                return mode.StopMode;
            }
        }

        return ValidationStopMode.CollectAll;
    }
}
