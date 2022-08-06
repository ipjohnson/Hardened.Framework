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
            IncrementalValuesProvider<ApplicationSelector.Model> incrementalValuesProvider)
        {
            initializationContext.RegisterSourceOutput(
                incrementalValuesProvider,
                ModelWriter
            );
        }

        private static void ModelWriter(SourceProductionContext arg1, ApplicationSelector.Model entryPoint)
        {
            var applicationFile = ApplicationFileWriter.WriteFile(entryPoint);

            File.WriteAllText(@"c:\temp\generated\Function.Application.App.cs", applicationFile);

            arg1.AddSource(entryPoint.ApplicationType.Name + ".App", applicationFile);
        }
    }
}
