using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// A filter that can check its constraints over parameters that did not all bind.
/// </summary>
/// <remarks>
/// Asked by the binding filter when binding failed, which is the one time the chain does not reach
/// the validation filter on its own. See <see cref="ParameterBindingException"/>.
/// </remarks>
internal interface IBoundParameterConstraints
{
    /// <returns>The constraint errors, or null when this filter has none to add.</returns>
    ValidationModules.ValidationResult? Check(IExecutionRequestParameters parameters);
}
