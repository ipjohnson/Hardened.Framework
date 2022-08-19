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
            PathToken? pathToken)
        {
            ParameterType = parameterType;
            Name = name;
            Required = required;
            DefaultValue = defaultValue;
            BindingType = bindingType;
            PathToken = pathToken;
        }

        public ITypeDefinition ParameterType { get; }

        public string Name { get; }

        public bool Required { get; }

        public string? DefaultValue { get; }

        public ParameterBindType BindingType { get; }

        public PathToken? PathToken { get; }
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
