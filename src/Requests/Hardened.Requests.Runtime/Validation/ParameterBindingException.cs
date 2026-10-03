using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Thrown when one or more of a request's parameters would not bind, carrying what did.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ValidationException"/>, so it answers as every other field error does. What it adds
/// is <see cref="Parameters"/>: the binding filter checks the handler's constraints over the
/// parameters that bound and reports those failures beside these, so <c>?pageSize=101&amp;status=bogus</c>
/// names both fields in one response.
/// </para>
/// <para>
/// A constraint error on a field that failed to bind is dropped. That field holds its default
/// rather than what the caller sent, so a <c>required</c> or <c>range</c> against it would describe a
/// value nobody sent.
/// </para>
/// </remarks>
public sealed class ParameterBindingException : ValidationException
{
    private readonly IReadOnlyCollection<string> _fields;

    private ParameterBindingException(
        ValidationModules.ValidationResult result,
        IExecutionRequestParameters parameters,
        IReadOnlyCollection<string> fields,
        Exception? inner
    )
        : base(result, inner!)
    {
        Parameters = parameters;
        _fields = fields;
    }

    /// <summary>
    /// The parameters that bound, with each one that failed left at its default.
    /// </summary>
    public IExecutionRequestParameters Parameters { get; }

    internal static ParameterBindingException From(
        IReadOnlyList<ValidationException> failures,
        IExecutionRequestParameters parameters
    )
    {
        var errors = failures.SelectMany(failure => failure.ValidationResult.Errors).ToArray();
        var fields = new HashSet<string>(
            errors.Select(error => error.Field),
            StringComparer.Ordinal
        );

        // The first conversion failure's cause, for the log. A missing value has none.
        var inner = failures
            .Select(failure => failure.InnerException)
            .FirstOrDefault(cause => cause != null);

        return new ParameterBindingException(
            ValidationModules.ValidationResult.FromErrors(errors),
            parameters,
            fields,
            inner
        );
    }

    /// <summary>
    /// This failure with the constraint errors over the parameters that bound added after it.
    /// </summary>
    internal ParameterBindingException WithConstraints(
        ValidationModules.ValidationResult constraints
    )
    {
        var kept = constraints.Errors.Where(error => !Failed(error.Field)).ToArray();

        if (kept.Length == 0)
        {
            return this;
        }

        return new ParameterBindingException(
            ValidationResult.Merge(ValidationModules.ValidationResult.FromErrors(kept)),
            Parameters,
            _fields,
            InnerException
        );
    }

    private bool Failed(string field)
    {
        foreach (var failed in _fields)
        {
            if (
                field.Length == failed.Length
                    ? string.Equals(field, failed, StringComparison.Ordinal)
                    : field.Length > failed.Length
                        && field.StartsWith(failed, StringComparison.Ordinal)
                        && field[failed.Length] is '.' or '['
            )
            {
                return true;
            }
        }

        return false;
    }
}
