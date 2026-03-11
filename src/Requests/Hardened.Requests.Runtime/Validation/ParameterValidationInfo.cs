using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation;

public class ParameterValidationInfo {
    public ParameterValidationInfo(string parameterName, IValidationRule[] rules) {
        ParameterName = parameterName;
        Rules = rules;
    }

    public string ParameterName { get; }

    public IValidationRule[] Rules { get; }
}
