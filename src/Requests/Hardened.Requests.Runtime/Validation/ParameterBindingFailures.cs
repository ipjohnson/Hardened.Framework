using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Every parameter of one request that would not bind.
/// </summary>
/// <remarks>
/// <para>
/// The generated binder catches each parameter's failure into this and carries on to the next, so
/// <c>?pageSize=abc&amp;status=bogus</c> names both rather than whichever was declared first.
/// </para>
/// <para>
/// A struct whose list is made on the first failure, so a request that binds allocates nothing for
/// it. It lives in a local of the binder, which is what lets <see cref="Add"/> mutate it.
/// </para>
/// </remarks>
public struct ParameterBindingFailures
{
    private List<ValidationException>? _failures;

    public void Add(ValidationException failure) => (_failures ??= new()).Add(failure);

    /// <summary>
    /// Records a body parameter's failure.
    /// </summary>
    /// <returns>
    /// The body as far as it was read, where the failure carries one, so the binder can still put
    /// it on the parameters. See <see cref="BodyBindingException"/>.
    /// </returns>
    public T? AddBody<T>(ValidationException failure)
    {
        Add(failure);

        return failure is BodyBindingException { Body: T body } ? body : default;
    }

    /// <summary>
    /// Throws <see cref="ParameterBindingException"/> when anything failed.
    /// </summary>
    /// <param name="parameters">
    /// What did bind, with each failed parameter left at its default, so the constraints on the
    /// others can still be checked.
    /// </param>
    public readonly void ThrowIfAny(IExecutionRequestParameters parameters)
    {
        if (_failures != null)
        {
            throw ParameterBindingException.From(_failures, parameters);
        }
    }
}
