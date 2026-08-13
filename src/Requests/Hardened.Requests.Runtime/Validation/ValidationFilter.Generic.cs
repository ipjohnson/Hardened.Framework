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
public sealed class ValidationFilter<TValidated> : IExecutionFilter where TValidated : class {
    private readonly IReadOnlyList<IValidatorFor<TValidated>> _validators;

    public ValidationFilter(IReadOnlyList<IValidatorFor<TValidated>> validators) {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        if (context.Request.Parameters is not TValidated target) {
            await chain.Next();
            return;
        }

        var collector = new ValidationErrorCollector();

        // Every validator for the type runs into one collector, so results merge rather than one
        // replacing another - plan §8. A hand-written validator adds to the structural checks; it
        // cannot suppress them.
        foreach (var validator in _validators) {
            validator.ValidateInto(collector, target);
        }

        // Structural first, and async only if it passed: an async rule is there to ask a question of
        // something outside the process - is this SKU taken - and asking it about a field that is
        // null or malformed is a round trip for an answer nobody needs.
        if (collector.HasErrors) {
            throw new ValidationException(collector.ToResult());
        }

        await RunAsyncValidators(context, collector, target);

        if (collector.HasErrors) {
            throw new ValidationException(collector.ToResult());
        }

        await chain.Next();
    }

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
        IExecutionContext context, ValidationErrorCollector collector, TValidated target) {
        var validators = context.RequestServices.GetServices<IAsyncValidatorFor<TValidated>>();

        foreach (var validator in validators) {
            var validationContext = new ValidationContext(collector);

            await validator.ValidateAsync(validationContext, target, context.CancellationToken);
        }
    }
}
