using System;
using System.Collections.Generic;
using System.Text;
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
