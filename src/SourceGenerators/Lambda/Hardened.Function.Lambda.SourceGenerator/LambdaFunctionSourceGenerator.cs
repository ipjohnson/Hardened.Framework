using System;
using System.Collections.Generic;
using System.Text;
using Hardened.SourceGenerator.Configuration;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Generator;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    [Generator]
    public class LambdaFunctionSourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(true)
            );
            
            LambdaEntryIncrementalGenerator.Setup(context, applicationModel);
            //LambdaFunctionApplicationBootstrapGenerator.Setup(context, applicationModel);
        }
    }
}
