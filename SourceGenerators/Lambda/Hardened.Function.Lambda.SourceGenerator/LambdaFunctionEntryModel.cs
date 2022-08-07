using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaFunctionEntryModel
    {
        public LambdaFunctionEntryModel(string name, ITypeDefinition handler, ResponseInformation responseInformation, IReadOnlyList<LambdaFunctionParameterModel> parameters, IReadOnlyList<FilterInformationModel> filters)
        {
            Name = name;
            Handler = handler;
            ResponseInformation = responseInformation;
            Parameters = parameters;
            Filters = filters;
        }

        public string Name { get; }

        public ITypeDefinition Handler { get; }
        
        public ResponseInformation ResponseInformation { get; }

        public IReadOnlyList<LambdaFunctionParameterModel> Parameters { get; }

        public IReadOnlyList<FilterInformationModel> Filters { get; }
    }

    public class LambdaFunctionParameterModel
    {
        public LambdaFunctionParameterModel(string name, ITypeDefinition parameterType, LambdaFunctionParameterBinding binding, string? bindingName)
        {
            Name = name;
            ParameterType = parameterType;
            Binding = binding;
            BindingName = bindingName;
        }

        public string Name { get; }

        public ITypeDefinition ParameterType { get; }

        public LambdaFunctionParameterBinding Binding { get; }

        public string? BindingName { get; }
    }

    public enum LambdaFunctionParameterBinding
    {
        Body,
        CustomContext
    }
}
