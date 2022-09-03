using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Module
{
    public class ModuleEntryPointFileWriter
    {
        public static void WriteFile(SourceProductionContext context, EntryPointSelector.Model model)
        {
            try
            {

                File.AppendAllText(@"c:\temp\generated\module.txt",  "Starting \r\n");
                var csharpFile = new CSharpFileDefinition(model.EntryPointType.Namespace);

                GenerateClassDefinition(context, model, csharpFile);

                var outputContext = new OutputContext();

                csharpFile.WriteOutput(outputContext);

                File.AppendAllText(@"C:\temp\generated\" + model.EntryPointType.Name + ".Module.cs",
                    outputContext.Output());

                context.AddSource(model.EntryPointType.Name + ".Module.cs", outputContext.Output());
            }
            catch (Exception exp)
            {
                File.AppendAllText(@"c:\temp\generated\module.txt", exp.Message + "\r\n");
            }
        }

        private static void GenerateClassDefinition(SourceProductionContext context, EntryPointSelector.Model model, CSharpFileDefinition csharpFile)
        {
            var moduleClass = csharpFile.AddClass(model.EntryPointType.Name);

            moduleClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

            moduleClass.AddBaseType(KnownTypes.Application.IApplicationModule);

            GenerateConfigureMethod(context, model, moduleClass);
        }

        private static void GenerateConfigureMethod(SourceProductionContext context, EntryPointSelector.Model model, ClassDefinition moduleClass)
        {
            var configureModuleMethod = moduleClass.AddMethod("ConfigureModule");

            var environment =
                configureModuleMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var serviceCollection =
                configureModuleMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

            var modulesMethod = model.MethodDefinitions.FirstOrDefault(m => m.Name == "Modules");

            if (modulesMethod != null)
            {
                GenerateModulesForeach(configureModuleMethod, environment, serviceCollection, model, modulesMethod);
            }

            configureModuleMethod.AddIndentedStatement(Invoke("ConfigureServiceCollection", environment, serviceCollection));
        }

        private static void GenerateModulesForeach(MethodDefinition configureModuleMethod, ParameterDefinition environment, ParameterDefinition serviceCollection, EntryPointSelector.Model model, HardenedMethodDefinition modulesMethod)
        {
            InvokeDefinition invokeDefinition =
                modulesMethod.Parameters.Count == 0 ?
                    Invoke("Modules") :
                    Invoke("Modules", environment);

            var forEach = configureModuleMethod.ForEach("module", invokeDefinition);

            forEach.AddIndentedStatement(
                forEach.Instance.Invoke("ConfigureModule", environment, serviceCollection));
        }
    }
}
