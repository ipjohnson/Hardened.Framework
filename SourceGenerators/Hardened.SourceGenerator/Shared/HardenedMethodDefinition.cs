using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared
{
    public class HardenedMethodDefinition
    {
        public HardenedMethodDefinition(string name, ITypeDefinition? returnType, IReadOnlyCollection<HardenedParameterDefinition> parameters)
        {
            Name = name;
            ReturnType = returnType;
            Parameters = parameters;
        }

        public string Name { get; }

        public ITypeDefinition? ReturnType { get; }

        public IReadOnlyCollection<HardenedParameterDefinition> Parameters { get; }
    }

    public class HardenedParameterDefinition
    {
        public HardenedParameterDefinition(string name, ITypeDefinition type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }

        public ITypeDefinition Type { get; }
    }
}
