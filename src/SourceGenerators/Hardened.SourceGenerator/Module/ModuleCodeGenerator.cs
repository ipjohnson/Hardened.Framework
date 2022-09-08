using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Module
{
    public class ModuleCodeGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext context,
            IncrementalValuesProvider<EntryPointSelector.Model> applicationModel)
        {
            context.RegisterSourceOutput(
                applicationModel,
                ModuleEntryPointFileWriter.WriteFile
            );
        }
    }
}
