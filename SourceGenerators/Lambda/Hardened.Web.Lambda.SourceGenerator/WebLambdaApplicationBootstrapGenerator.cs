﻿using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Web.Lambda.SourceGenerator
{
    public static class WebLambdaApplicationBootstrapGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<EntryPointSelector.Model> incrementalValuesProvider)
        {
            initializationContext.RegisterSourceOutput(
                incrementalValuesProvider,
                ModelWriter
                );
        }

        private static void ModelWriter(SourceProductionContext arg1, EntryPointSelector.Model entryPoint)
        {
            var applicationFile = ApplicationFileWriter.WriteFile(entryPoint);

            File.WriteAllText(@"c:\temp\Application.App.cs", applicationFile);

            arg1.AddSource(entryPoint.EntryPointType.Name + ".App", applicationFile);
        }
        
    }
}
