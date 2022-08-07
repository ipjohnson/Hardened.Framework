using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request
{
    public class FilterInformationModel
    {
        public FilterInformationModel(ITypeDefinition typeDefinition, string arguments, string propertyAssignment)
        {
            TypeDefinition = typeDefinition;
            Arguments = arguments;
            PropertyAssignment = propertyAssignment;
        }

        public ITypeDefinition TypeDefinition { get; }

        public string Arguments { get; }

        public string PropertyAssignment { get; }
    }
}
