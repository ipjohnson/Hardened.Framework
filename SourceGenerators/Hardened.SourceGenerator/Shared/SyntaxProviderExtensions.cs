using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Shared
{
    public static class SyntaxProviderExtensions
    {
        public static IncrementalValuesProvider<ApplicationSelector.Model> GetEntryPoint(
            this IncrementalGeneratorInitializationContext initializationContext, string libraryAttribute)
        {
            return initializationContext.SyntaxProvider.CreateSyntaxProvider(
                ApplicationSelector.UsingAttribute(libraryAttribute),
                ApplicationSelector.TransformModel
            );
        }
    }
}
