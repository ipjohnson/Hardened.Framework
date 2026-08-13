using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using ValidationModules;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Attaches a validator that was named in generated source rather than looked up.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="ValidateAttribute{TValidated}"/>, and the difference between them
/// is what the validated type is. The attribute resolves, because its type parameter is something a
/// consumer can see and write a validator for - a parameters interface, a body model - and resolving
/// is what lets a hand-written validator merge with the generated one. This hands the validator in,
/// because its type parameter is a handler's nested <c>Parameters</c> class: generated, named with a
/// computed suffix, and not a type anyone registers anything against.
/// </para>
/// <para>
/// <b>The hand-in is the point, not an optimisation.</b> Writing
/// <c>new ValidationFilterProvider&lt;Parameters&gt;(SomeValidator.Instance)</c> does not compile
/// unless that validator was emitted, so a filter cannot be attached to a handler whose validator is
/// missing. Resolution would make the same mistake a run-time question, and the answer - no
/// validators registered - is indistinguishable from a request that had nothing to check.
/// </para>
/// <para>
/// Constructed once. <c>GetFilters</c> runs from the handler's constructor, the routing table caches
/// handlers behind <c>??=</c> against the root provider, and the generated validator is a stateless
/// <c>static readonly Instance</c>. So the provider, the filter and the validator are each built
/// once per process and the request path does no container work and allocates nothing.
/// </para>
/// </remarks>
public sealed class ValidationFilterProvider<TValidated> : IRequestFilterProvider
    where TValidated : class {

    private readonly RequestFilterInfo _info;

    public ValidationFilterProvider(IValidatorFor<TValidated> validator) {
        if (validator == null) {
            throw new ArgumentNullException(nameof(validator));
        }

        var filter = new ValidationFilter<TValidated>(new[] { validator });

        _info = new RequestFilterInfo(_ => filter, FilterOrder.Validation);
    }

    public IEnumerable<RequestFilterInfo> GetFilters(IExecutionRequestHandlerInfo handlerInfo) {
        yield return _info;
    }
}
