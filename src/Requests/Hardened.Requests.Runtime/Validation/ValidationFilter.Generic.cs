using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;
using ValidationModules;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Validates the request's bound parameters against <typeparamref name="TValidated"/> and throws if
/// they do not hold.
/// </summary>
/// <remarks>
/// <para>
/// <typeparamref name="TValidated"/> is an interface the generated <c>Parameters</c> class
/// implements, named by whatever produced the validator. The pipeline's own type parameter stays the
/// concrete <c>Parameters</c>: making it this interface instead would force the interface to declare
/// every parameter rather than the validated ones, put interface dispatch on the invoke path of
/// every handler, and allow exactly one validated type per handler.
/// </para>
/// <para>
/// The validators are resolved once and handed in - see <see cref="ValidateAttribute{T}"/>. They are
/// registered as singletons, so the set is stable for the life of the process and there is no
/// container work per request.
/// </para>
/// </remarks>
public sealed class ValidationFilter<TValidated> : IExecutionFilter
    where TValidated : class
{
    private readonly IReadOnlyList<IValidatorFor<TValidated>> _validators;

    private readonly ValidationStopMode _stopMode;

    public ValidationFilter(IReadOnlyList<IValidatorFor<TValidated>> validators)
        : this(validators, ValidationStopMode.CollectAll) { }

    /// <param name="validators">Every validator registered for the type.</param>
    /// <param name="stopMode">
    /// Whether the pass stops at its first failure, as the handler's
    /// <see cref="ValidationModeAttribute"/> declares.
    /// </param>
    public ValidationFilter(
        IReadOnlyList<IValidatorFor<TValidated>> validators,
        ValidationStopMode stopMode
    )
    {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
        _stopMode = stopMode;
    }

    public async Task Execute(IExecutionChain chain)
    {
        var context = chain.Context;

        // Not a guard - an assertion. This filter is in the chain because something declared
        // constraints, so parameters that are absent or of another shape mean the value those
        // constraints were written about is not being checked. Continuing would answer the request
        // normally with nothing validated, which is the one failure this design refuses to have:
        // silent, indistinguishable from a request that passed. Whoever attached the filter and
        // whoever bound the parameters disagree, and that is a build-time defect wearing a
        // runtime disguise.
        if (context.Request.Parameters is not TValidated target)
        {
            throw new InvalidOperationException(
                $"The request's parameters are {Describe(context.Request.Parameters)}, but this "
                    + $"handler is attached to a validation filter for {typeof(TValidated).FullName}. "
                    + "Nothing would have been validated. A generator emits the filter and the parameters "
                    + "class together, so this means the two came from different builds."
            );
        }

        var collector = new ValidationErrorCollector { StopMode = _stopMode };

        // Every validator for the type runs into one collector, so results merge rather than one
        // replacing another - plan §8. A hand-written validator adds to the structural checks; it
        // cannot suppress them. Under StopOnFirstError the collector closes at its first error, and
        // a validator after that would only have what it found dropped.
        foreach (var validator in _validators)
        {
            validator.ValidateInto(collector, target);

            if (Stopped(collector))
            {
                break;
            }
        }

        // Structural first, and async only if it passed: an async rule is there to ask a question of
        // something outside the process - is this SKU taken - and asking it about a field that is
        // null or malformed is a round trip for an answer nobody needs.
        if (collector.HasErrors)
        {
            throw new ValidationException(collector.ToResult());
        }

        await RunAsyncValidators(context, collector, target, Stopped);

        if (collector.HasErrors)
        {
            throw new ValidationException(collector.ToResult());
        }

        await chain.Next();
    }

    /// <summary>
    /// Whether this pass has what it was asked for, and the next validator need not run.
    /// </summary>
    /// <remarks>
    /// An error rather than any failure, because a warning does not stop a pass in ValidationModules
    /// either, and stopping on one would skip a later validator's error. <c>HasErrors</c> counts
    /// warnings as well, so the result is only built once something has failed.
    /// </remarks>
    private bool Stopped(ValidationErrorCollector collector) =>
        _stopMode == ValidationStopMode.StopOnFirstError
        && collector.HasErrors
        && !collector.ToResult().IsValid;

    private static string Describe(IExecutionRequestParameters? parameters) =>
        parameters is null ? "null" : parameters.GetType().FullName ?? parameters.GetType().Name;

    /// <summary>
    /// Runs any <see cref="IAsyncValidatorFor{T}"/> the container has for this type.
    /// </summary>
    /// <remarks>
    /// Resolved per request rather than cached, because these are the validators most likely to be
    /// registered scoped - one that reaches a database holds a connection from the request's scope,
    /// and caching the first request's instance would pin that scope for the life of the process.
    /// The lookup returns nothing in the common case and costs a dictionary hit.
    /// </remarks>
    private static async Task RunAsyncValidators(
        IExecutionContext context,
        ValidationErrorCollector collector,
        TValidated target,
        Func<ValidationErrorCollector, bool> stopped
    )
    {
        var validators = context.RequestServices.GetServices<IAsyncValidatorFor<TValidated>>();

        foreach (var validator in validators)
        {
            var validationContext = new ValidationContext(collector);

            await validator.ValidateAsync(validationContext, target, context.CancellationToken);

            if (stopped(collector))
            {
                break;
            }
        }
    }
}
