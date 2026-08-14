using System.Collections.Immutable;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using ValidationModules.SourceGenerator.Impl.Models;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.Validation.SourceGenerator;

/// <summary>
/// Registers every generated validator into the application's entry point.
/// </summary>
/// <remarks>
/// <para>
/// A partial of the entry point class, adding to
/// <c>DependencyRegistry&lt;TEntryPoint&gt;</c> - the same mechanism the OpenAPI routing table uses,
/// and the reason a consumer calls nothing. DependencyModules composes sibling modules only through
/// attributes someone writes, so a module emitted next to the entry point would never be loaded.
/// </para>
/// <para>
/// Registered as singletons, matching how the validators are emitted: a stateless
/// <c>static readonly Instance</c> with a private constructor. The filter resolves
/// <c>IEnumerable&lt;IValidatorFor&lt;T&gt;&gt;</c>, so a hand-written validator a consumer
/// registers for the same type runs alongside the generated one rather than replacing it.
/// </para>
/// </remarks>
internal static class RegistrationWriter {

    private static readonly ITypeDefinition ServiceCollection =
        TypeDefinition.Get("Microsoft.Extensions.DependencyInjection", "IServiceCollection");

    public static void Write(
        SourceProductionContext context,
        EntryPointSelector.Model entryPoint,
        ImmutableArray<ValidatedTypeModel> validators) {
        if (validators.Length == 0) {
            return;
        }

        var file = new CSharpFileDefinition(entryPoint.EntryPointType.Namespace);

        var appClass = file.AddClass(entryPoint.EntryPointType.Name);
        appClass.Modifiers |= ComponentModifier.Partial;

        var field = appClass.AddField(typeof(int), "_validationModuleDependencies");
        field.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;
        field.AddUsingNamespace("DependencyModules.Runtime.Helpers");
        field.InitializeValue = new CodeOutputComponent(
            $"DependencyRegistry<{entryPoint.EntryPointType.Name}>.Add(ValidationModuleDI)");

        // Without this the field initializer is trimmed and nothing is ever registered - the same
        // guard the routing table carries.
        field.AddAttribute(
            TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"),
            new CodeOutputComponent("nameof(ValidationModuleDI)") { Indented = false });

        var method = appClass.AddMethod("ValidationModuleDI");
        method.Modifiers |= ComponentModifier.Private | ComponentModifier.Static;

        var services = method.AddParameter(ServiceCollection, "serviceCollection");

        // Ordered so the emitted table does not reshuffle between builds, which would turn every
        // incremental compile into a diff.
        foreach (var validator in validators
                     .OrderBy(v => v.Namespace, StringComparer.Ordinal)
                     .ThenBy(v => v.ValidatorName, StringComparer.Ordinal)) {
            var qualified = validator.Namespace.Length == 0
                ? validator.ValidatorName
                : validator.Namespace + "." + validator.ValidatorName;

            method.AddIndentedStatement(new CodeOutputComponent(
                $"serviceCollection.AddSingleton<global::ValidationModules.IValidatorFor<{validator.QualifiedTypeName}>>(" +
                $"global::{qualified}.Instance)") { Indented = false });
        }

        _ = services;

        var outputContext = new OutputContext(new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global });

        file.WriteOutput(outputContext);

        context.AddSource(entryPoint.EntryPointType.Name + ".ValidationModule.g.cs", outputContext.Output());
    }
}
