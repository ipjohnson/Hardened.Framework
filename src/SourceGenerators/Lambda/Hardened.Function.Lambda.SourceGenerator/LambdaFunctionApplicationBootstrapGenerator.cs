using System;
using System.Collections.Generic;
using System.Text;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Function.Lambda.SourceGenerator
{
    public class LambdaFunctionApplicationBootstrapGenerator
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
            
            arg1.AddSource(entryPoint.EntryPointType.Name + ".App", applicationFile);
        }
    }
}
