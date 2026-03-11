using Hardened.Requests.Abstract.Validation;

namespace Hardened.Requests.Runtime.Validation;

public class PropertyValidationInfo {
    public PropertyValidationInfo(string propertyName, IValidationRule[] rules, Func<object, object?> accessor) {
        PropertyName = propertyName;
        Rules = rules;
        Accessor = accessor;
    }

    public string PropertyName { get; }

    public IValidationRule[] Rules { get; }

    public Func<object, object?> Accessor { get; }
}
