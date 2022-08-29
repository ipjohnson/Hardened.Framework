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
            var dependencyRegistry = new[]
            {
                KnownTypes.DI.Registry.StandardDependencies,
                KnownTypes.DI.Registry.RequestRuntimeDI,
                KnownTypes.DI.Registry.TemplateDI,
                KnownTypes.DI.Registry.LambdaFunctionRuntimeDI
            };

            var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute("LambdaFunctionApplication"),
                EntryPointSelector.TransformModel(true)
            );

            DependencyInjectionIncrementalGenerator.Setup(context, applicationModel, dependencyRegistry);
            ConfigurationIncrementalGenerator.Setup(context, applicationModel);
            TemplateIncrementalGenerator.Setup(context, applicationModel, new[] { "html" });

            LambdaEntryIncrementalGenerator.Setup(context, applicationModel);
            LambdaFunctionApplicationBootstrapGenerator.Setup(context, applicationModel);
        }
    }
}
