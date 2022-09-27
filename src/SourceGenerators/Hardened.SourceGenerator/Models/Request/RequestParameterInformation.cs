using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request
{
    public class RequestParameterInformation
    {
        public RequestParameterInformation(
            ITypeDefinition parameterType,
            string name, 
            bool required, 
            string? defaultValue,
            ParameterBindType bindingType, 
            string bindingName)
        {
            ParameterType = parameterType;
            Name = name;
            Required = required;
            DefaultValue = defaultValue;
            BindingType = bindingType;
            BindingName = bindingName;
        }

        public ITypeDefinition ParameterType { get; }

        public string Name { get; }

        public bool Required { get; }

        public string? DefaultValue { get; }

        public ParameterBindType BindingType { get; }

        public string BindingName { get; }

        public override bool Equals(object obj)
        {
            if (obj is not RequestParameterInformation requestParameterInformation)
            {
                return false;
            }

            if (!ParameterType.Equals(requestParameterInformation.ParameterType))
            {
                return false;
            }

            if (!Name.Equals(requestParameterInformation.Name))
            {
                return false;
            }

            if (!Required.Equals(requestParameterInformation.Required))
            {
                return false;
            }

            if (DefaultValue != requestParameterInformation.DefaultValue)
            {
                return false;
            }

            if (!BindingType.Equals(requestParameterInformation.BindingType))
            {
                return false;
            }

            if (!BindingName.Equals(requestParameterInformation.BindingName))
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return $"{ParameterType} {Name}";
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ParameterType.GetHashCode();
                hashCode = (hashCode * 397) ^ Name.GetHashCode();
                hashCode = (hashCode * 397) ^ Required.GetHashCode();
                hashCode = (hashCode * 397) ^ (DefaultValue != null ? DefaultValue.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)BindingType;
                hashCode = (hashCode * 397) ^ BindingName.GetHashCode();
                return hashCode;
            }
        }
    }

    public enum ParameterBindType
    {
        Path,
        QueryString,
        Header,
        Body,
        ServiceProvider,
        ExecutionContext,
        ExecutionRequest,
        ExecutionResponse
    }
}
