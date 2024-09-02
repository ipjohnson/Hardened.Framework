using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Models.Request;

public class RequestParameterInformation {
    public RequestParameterInformation(
        ITypeDefinition parameterType,
        string name,
        bool required,
        string? defaultValue,
        ParameterBindType bindingType,
        string bindingName,
        int parameterIndex,
        AttributeModel? customAttribute = null) {
        ParameterType = parameterType;
        Name = name;
        Required = required;
        DefaultValue = defaultValue;
        BindingType = bindingType;
        BindingName = bindingName;
        ParameterIndex = parameterIndex;
        CustomAttribute = customAttribute;
    }

    public ITypeDefinition ParameterType { get; }

    public string Name { get; }

    public bool Required { get; }

    public string? DefaultValue { get; }
    
    public AttributeModel? CustomAttribute { get; }

    public ParameterBindType BindingType { get; }

    public string BindingName { get; }

    public int ParameterIndex {
        get;
    }

    public override bool Equals(object obj) {
        if (obj is not RequestParameterInformation requestParameterInformation) {
            return false;
        }

        if (!ParameterType.Equals(requestParameterInformation.ParameterType)) {
            return false;
        }

        if (!Name.Equals(requestParameterInformation.Name)) {
            return false;
        }

        if (!Required.Equals(requestParameterInformation.Required)) {
            return false;
        }

        if (DefaultValue != requestParameterInformation.DefaultValue) {
            return false;
        }

        if (!BindingType.Equals(requestParameterInformation.BindingType)) {
            return false;
        }

        if (!BindingName.Equals(requestParameterInformation.BindingName)) {
            return false;
        }

        if (CustomAttribute != null &&
            !CustomAttribute.Equals(requestParameterInformation.CustomAttribute)) {
            return false;
        }

        return true;
    }

    public override string ToString() {
        return $"{ParameterType} {Name}";
    }

    public override int GetHashCode() {
        unchecked {
            var hashCode = ParameterType.GetHashCode();
            hashCode = (hashCode * 397) ^ Name.GetHashCode();
            hashCode = (hashCode * 397) ^ Required.GetHashCode();
            hashCode = (hashCode * 397) ^ (DefaultValue != null ? DefaultValue.GetHashCode() : 0);
            hashCode = (hashCode * 397) ^ (int)BindingType;
            hashCode = (hashCode * 397) ^ BindingName.GetHashCode();
            
            if (CustomAttribute is not null) {
                hashCode = (hashCode * 397) ^ CustomAttribute.GetHashCode();
            }
            
            return hashCode;
        }
    }
}

public enum ParameterBindType {
    Path,
    QueryString,
    Header,
    Body,
    ServiceProvider,
    FromServiceProvider,
    ExecutionContext,
    ExecutionRequest,
    ExecutionResponse,
    CustomAttribute,
}