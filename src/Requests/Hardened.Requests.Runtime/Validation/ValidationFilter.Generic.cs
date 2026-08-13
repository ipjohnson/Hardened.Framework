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
/// The structural validator is handed in rather than resolved. Generated validators are stateless
/// singletons, so in the ordinary case this filter does no container work at all - see
/// <see cref="ValidationFilterProvider{T}"/> for why there is none per request either.
/// </para>
/// </remarks>
public sealed class ValidationFilter<TValidated> : IExecutionFilter where TValidated : class {
    private readonly IValidatorFor<TValidated> _validator;

    public ValidationFilter(IValidatorFor<TValidated> validator) {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        if (context.Request.Parameters is not TValidated target) {
            await chain.Next();
            return;
        }

        var collector = new ValidationErrorCollector();

        _validator.ValidateInto(collector, target);

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
