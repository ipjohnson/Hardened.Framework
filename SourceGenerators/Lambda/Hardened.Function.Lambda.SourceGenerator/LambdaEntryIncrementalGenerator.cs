using System;
using System.Collections.Generic;
using System.Text;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Function.Lambda.SourceGenerator
{
    internal class LambdaEntryIncrementalGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<ApplicationSelector.Model> applicationValuesProvider)
        {
            var methodSelector = new SyntaxSelector<MethodDeclarationSyntax>(KnownTypes.Lambda.LambdaFunctionAttribute);

            var modelProvider = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                methodSelector.Where,
                LambdaFunctionModelGenerator.GenerateWebModel
            );

            var applicationCollect = applicationValuesProvider.Collect();

            var invokeGenerator = new LambdaEntryPointFileWriter();
            
            initializationContext.RegisterSourceOutput(
                modelProvider.Combine(applicationCollect),
                invokeGenerator.GenerateSource
            );
        }
    }
}
