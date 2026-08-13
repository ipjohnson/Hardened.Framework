using System.Collections.Immutable;
using System.Linq;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ValidationModules.SourceGenerator.Impl;
using ValidationModules.SourceGenerator.Impl.Emitters;
using ValidationModules.SourceGenerator.Impl.FrontEnds;
using ValidationModules.SourceGenerator.Impl.Models;

namespace Hardened.Validation.SourceGenerator;

/// <summary>
/// Turns constraint attributes into validators, and registers them into the application's entry
/// point so nothing has to be wired by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>One scan, one path.</b> A model a developer wrote and a model the OpenAPI build task emitted
/// are the same thing here: both are ordinary source in the compilation, both carry attributes, and
/// both are read by ValidationModules' own front-end. There is no spec-aware code in this generator
/// at all - the task's job is to put attributes on what it emits, and this picks them up like
/// anything else. <c>[Required]</c> on a hand-written class needs no wiring beyond referencing the
/// package.
/// </para>
/// <para>
/// <b>Both attribute vocabularies.</b> ValidationModules' own constraints and
/// <c>System.ComponentModel.DataAnnotations</c> reach the same IR through the same front-end, so
/// <c>[StringLength(1, 100)]</c> from either namespace produces the same validator.
/// </para>
/// <para>
/// <b>Why registration lands in the entry point.</b> DependencyModules keys registrations on the
/// entry point type - <c>DependencyRegistry&lt;TEntryPoint&gt;</c> - and composes sibling modules
/// only through attributes someone writes. A module emitted beside the entry point would sit there
/// unreferenced. Naming the entry point and adding to its registry is what the routing table
/// already does, and it means a consumer calls nothing.
/// </para>
/// </remarks>
[Generator]
public class HardenedValidationGenerator : IIncrementalGenerator {

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => {
            provider.GlobalOptions.TryGetValue("build_property.ValidationModules_FieldNaming", out var naming);
            provider.GlobalOptions.TryGetValue("build_property.ValidationModules_DataAnnotations", out var dataAnnotations);
            provider.GlobalOptions.TryGetValue("build_property.ValidationModules_PatternPolicy", out var patternPolicy);
            provider.GlobalOptions.TryGetValue("build_property.PublishAot", out var publishAot);
            provider.GlobalOptions.TryGetValue("build_property.IsAotCompatible", out var aotCompatible);

            return new GeneratorOptions(naming, dataAnnotations, patternPolicy,
                IsTrue(publishAot) || IsTrue(aotCompatible));
        });

        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, _) =>
                    syntaxContext.SemanticModel.GetDeclaredSymbol(syntaxContext.Node) as INamedTypeSymbol)
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => symbol!);

        var models = candidates
            .Combine(options)
            .Select(static (pair, _) => BuildModel(pair.Left, pair.Right))
            .Where(static result => result.Model is not null || result.Diagnostics.Length > 0);

        context.RegisterSourceOutput(models, static (production, result) => {
            foreach (var diagnostic in result.Diagnostics) {
                production.ReportDiagnostic(diagnostic);
            }

            if (result.Model is { } model) {
                production.AddSource(HintNameFor(model), new ValidatorEmitter().Emit(model));
            }
        });

        var entryPoint = context.SyntaxProvider.CreateSyntaxProvider(
                EntryPointSelector.UsingAttribute(),
                EntryPointSelector.TransformModel(false))
            .WithComparer(new EntryPointSelector.Comparer());

        var validators = models
            .Select(static (result, _) => result.Model)
            .Where(static model => model is not null)
            .Select(static (model, _) => model!)
            .Collect();

        context.RegisterSourceOutput(entryPoint.Combine(validators), static (production, pair) =>
            RegistrationWriter.Write(production, pair.Left, pair.Right));
    }

    /// <summary>
    /// The file a validator is added under, which Roslyn requires to be unique per generator.
    /// </summary>
    /// <remarks>
    /// Qualified by namespace: the type name is not unique on its own, and two namespaces in one
    /// assembly each declaring a <c>Customer</c> would collide. <c>AddSource</c> throws on a
    /// duplicate, which fails the whole generator rather than the second type.
    /// </remarks>
    private static string HintNameFor(ValidatedTypeModel model) =>
        model.Namespace.Length == 0
            ? $"{model.ValidatorName}.g.cs"
            : $"{model.Namespace}.{model.ValidatorName}.g.cs";

    private static ModelResult BuildModel(INamedTypeSymbol symbol, GeneratorOptions options) {
        var frontEnd = new AttributeFrontEnd(
            options.CompileDataAnnotations, options.FieldNamer, options.ResolvedPatternPolicy);

        var model = frontEnd.Build(symbol, static type => $"{type.Name}Validator");

        return new ModelResult(model, frontEnd.Diagnostics.ToImmutableArray());
    }

    private sealed record ModelResult(ValidatedTypeModel? Model, ImmutableArray<Diagnostic> Diagnostics);

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private sealed record GeneratorOptions(
        string? Naming, string? DataAnnotations, string? PatternPolicySetting, bool IsAotFacing) {

        /// <summary>
        /// Auto gates on the project's own AOT posture rather than on PublishAot alone, which is
        /// only ever true in an executable - a class library holding the models would never see it.
        /// </summary>
        public PatternPolicy ResolvedPatternPolicy => PatternPolicySetting switch {
            "Error" => PatternPolicy.Error,
            "Warn" => PatternPolicy.Warn,
            "Allow" => PatternPolicy.Allow,
            _ => IsAotFacing ? PatternPolicy.Error : PatternPolicy.Allow,
        };

        public bool CompileDataAnnotations =>
            !string.Equals(DataAnnotations, "Ignore", StringComparison.OrdinalIgnoreCase);

        public Func<string, string> FieldNamer => Naming switch {
            "PascalCase" or "AsDeclared" => static name => name,
            "SnakeCase" => SnakeCase,
            _ => CamelCase,
        };

        private static string CamelCase(string name) =>
            name.Length == 0 || !char.IsUpper(name[0])
                ? name
                : char.ToLowerInvariant(name[0]) + name.Substring(1);

        private static string SnakeCase(string name) {
            var builder = new System.Text.StringBuilder(name.Length + 4);

            for (var i = 0; i < name.Length; i++) {
                var character = name[i];

                if (char.IsUpper(character)) {
                    var startsWord = i > 0 &&
                        (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));

                    if (startsWord) {
                        builder.Append('_');
                    }

                    builder.Append(char.ToLowerInvariant(character));
                } else {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }
}
