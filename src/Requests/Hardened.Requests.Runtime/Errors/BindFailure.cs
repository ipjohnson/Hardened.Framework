using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Validation;

namespace Hardened.Requests.Runtime.Errors;

/// <summary>
/// What a failure to bind a request's parameters is recorded as.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="FormatException"/> thrown while binding is about the request: a custom JSON
/// converter or binding attribute found a value it could not read, and the caller has to hear
/// which. Thrown from a handler, the same type is a server fault, and
/// <see cref="ExceptionToModelConverter"/> answers it 500 without its message. So the binding
/// filters make it a 400 here, keeping its message, with the original as the inner exception.
/// </para>
/// <para>
/// A <see cref="ParameterBindingException"/> has the handler's constraints checked over the
/// parameters that did bind, because the chain stops here and never reaches the validation filter.
/// </para>
/// </remarks>
internal static class BindFailure
{
    public static Exception For(
        Exception exception,
        IExecutionContext context,
        IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> constraintFilters
    ) =>
        exception switch
        {
            FormatException => new BadRequestException(exception.Message, exception),
            ParameterBindingException binding => WithConstraints(
                binding,
                context,
                constraintFilters
            ),
            _ => exception,
        };

    private static ParameterBindingException WithConstraints(
        ParameterBindingException binding,
        IExecutionContext context,
        IReadOnlyList<Func<IExecutionContext, IExecutionFilter>> constraintFilters
    )
    {
        foreach (var filterFunc in constraintFilters)
        {
            if (
                filterFunc(context) is IBoundParameterConstraints constraints
                && constraints.Check(binding.Parameters) is { HasErrors: true } result
            )
            {
                binding = binding.WithConstraints(result);
            }
        }

        return binding;
    }
}
