﻿using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.DependencyInjection
{
    public static class DependencyInjectionIncrementalGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<ApplicationSelector.Model> entryPointProvider,
            IReadOnlyList<ITypeDefinition> defaultLibraries)
        {
            var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.DI.ExposeAttribute);

            var services = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                classSelector.Where,
                GenerateServiceModel
            );

            var servicesCollection = services.Collect();

            var generator = new DependencyInjectionFileGenerator(defaultLibraries);
            
            initializationContext.RegisterSourceOutput(entryPointProvider.Combine(servicesCollection), generator.GenerateFile);
        }

        private static ServiceModel GenerateServiceModel(GeneratorSyntaxContext arg1, CancellationToken arg2)
        {
            var classDeclarationSyntax = (ClassDeclarationSyntax)arg1.Node;

            ITypeDefinition? exposeTypeDef = null;
            var expose = classDeclarationSyntax.DescendantNodes().OfType<AttributeSyntax>()
                .FirstOrDefault(a => a.Name.ToString() == "Expose" || a.Name.ToString() == "ExposeAttribute");

            if (expose != null)
            {
                var exposeType = classDeclarationSyntax.BaseList?.Types.FirstOrDefault();

                if (exposeType != null)
                {
                    var symbolType = arg1.SemanticModel.GetSymbolInfo(exposeType.Type, arg2);

                    if (symbolType.Symbol != null)
                    {
                        exposeTypeDef = TypeDefinition.Get(symbolType.Symbol!.ContainingNamespace.ToString(),
                            symbolType.Symbol.Name);
                    }
                    else
                    {
                        // todo: handler error case
                    }
                }
            }

            var classTypeDef = TypeDefinition.Get(classDeclarationSyntax.GetNamespace(),
                classDeclarationSyntax.Identifier.ToString());

            File.AppendAllText(@"C:\temp\generated\ServiceModel." + classDeclarationSyntax.Identifier.Value + ".txt", exposeTypeDef!.Name + " " + classTypeDef .Name+ "\r\n");

            return new ServiceModel(exposeTypeDef!, classTypeDef, ServiceModel.ServiceLifestyle.Singleton);
        }

        public class ServiceModel
        {
            public ServiceModel(ITypeDefinition serviceType, ITypeDefinition implementationType, ServiceLifestyle lifestyle)
            {
                ServiceType = serviceType;
                ImplementationType = implementationType;
                Lifestyle = lifestyle;
            }

            public enum ServiceLifestyle
            {
                Transient,
                Scoped,
                Singleton
            }

            public ITypeDefinition ServiceType { get; }

            public ITypeDefinition ImplementationType { get; }

            public ServiceLifestyle Lifestyle { get; }
        }
    }
}
