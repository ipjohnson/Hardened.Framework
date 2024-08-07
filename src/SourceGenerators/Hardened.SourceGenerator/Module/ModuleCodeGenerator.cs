﻿using Microsoft.CodeAnalysis;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Module;

public class ModuleCodeGenerator {
    public static void Setup(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<EntryPointSelector.Model> applicationModel) {
        context.RegisterSourceOutput(
            applicationModel,
            SourceGeneratorWrapper.Wrap<EntryPointSelector.Model>(ModuleEntryPointFileWriter.WriteFile)
        );
    }
}