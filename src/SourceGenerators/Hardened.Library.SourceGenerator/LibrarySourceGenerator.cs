using Microsoft.CodeAnalysis;
using System;
using CSharpAuthor;
using Hardened.SourceGenerator.Configuration;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Module;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Library.SourceGenerator
{
    [Generator]
    public class LibrarySourceGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var dependencyRegistry = new[]
            {
                KnownTypes.DI.Registry.StandardDependencies
            };

            var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(false)
            ).WithComparer(new EntryPointSelector.Comparer());

            DependencyInjectionIncrementalGenerator.Setup(context, applicationModel, dependencyRegistry);
            ConfigurationIncrementalGenerator.Setup(context, applicationModel);
            //ModuleCodeGenerator.Setup(context, applicationModel);
        }
    }
}
