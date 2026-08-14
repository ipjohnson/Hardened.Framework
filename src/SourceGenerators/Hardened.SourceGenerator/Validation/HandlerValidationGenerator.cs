using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ValidationModules.SourceGenerator.Impl.Emitters;
using ValidationModules.SourceGenerator.Impl.Models;

namespace Hardened.SourceGenerator.Validation;

/// <summary>
/// Attaches validation to hand-written handlers: the validator for the generated
/// <c>Parameters</c> class, the filter that runs it, and warnings for constraints written where
/// this generator does not read them.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the front half of a web or function pipeline rather than sitting beside it,
/// because the handler model it produces carries the filter. Two providers reading the same
/// handler and reaching their own conclusions about whether it validates is the arrangement that
/// drifts.
/// </para>
/// </remarks>
public static class HandlerValidationGenerator {

    /// <summary>
    /// The handler models for a front-end, with validation attached, plus the source output that
    /// emits the validators they name.
    /// </summary>
    public static IncrementalValuesProvider<RequestHandlerModel> Setup(
        IncrementalGeneratorInitializationContext initializationContext,
        BaseRequestModelGenerator modelGenerator,
        Func<SyntaxNode, CancellationToken, bool> selector) {

        // Selected down to a bool before it reaches the pipeline, so an edit anywhere in the
        // project does not invalidate every handler along with the compilation.
        var validationAvailable = initializationContext.CompilationProvider.Select(
            static (compilation, _) =>
                compilation.GetTypeByMetadataName(ValidationGeneratorOptions.MarkerTypeName) is not null);

        var options = initializationContext.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => ValidationGeneratorOptions.Read(provider))
            .Combine(validationAvailable);

        var resolved = initializationContext.SyntaxProvider
            .CreateSyntaxProvider(
                (node, token) => selector(node, token),
                (context, token) => Analyze(modelGenerator, context, token))
            .Combine(options)
            .Select(static (pair, token) =>
                Resolve(pair.Left, pair.Right.Left, pair.Right.Right, token))
            .WithComparer(ResolvedComparer.Instance);

        initializationContext.RegisterSourceOutput(
            resolved.Where(static result => result.Validator is not null || !result.Diagnostics.IsEmpty),
            static (production, result) => {
                foreach (var diagnostic in result.Diagnostics) {
                    production.ReportDiagnostic(diagnostic);
                }

                if (result.Validator is { } validator) {
                    production.AddSource(
                        $"{validator.Namespace}.{validator.ValidatorName}.g.cs",
                        new ValidatorEmitter().Emit(validator));
                }
            });

        return resolved
            .Select(static (result, _) => result.Handler)
            .WithComparer(new RequestHandlerModelComparer());
    }

    /// <summary>
    /// Everything that has to be read off the syntax node, before the build properties that decide
    /// what to do with it are known.
    /// </summary>
    /// <remarks>
    /// The parameter types cross into the next stage as symbols, which is what lets the decision
    /// there depend on build properties. Nothing else does: the diagnostics are settled here,
    /// because whether a constraint sits on a parameter is not a question any property changes.
    /// </remarks>
    private static Candidate Analyze(
        BaseRequestModelGenerator modelGenerator,
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken) {

        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var handler = modelGenerator.GenerateRequestModel(context, cancellationToken);

        return new Candidate(
            handler,
            HandlerValidationFrontEnd.ParameterTypesOf(context, methodDeclaration),
            UncompiledConstraints(context, methodDeclaration));
    }

    private static Resolved Resolve(
        Candidate candidate,
        ValidationGeneratorOptions options,
        bool validationAvailable,
        CancellationToken cancellationToken) {

        // Nothing emits validators for this compilation, so there is nothing to attach to and
        // nothing to warn about - a project that never opted into validation behaves as it did.
        if (!validationAvailable) {
            return new Resolved(candidate.Handler, null, ImmutableArray<Diagnostic>.Empty);
        }

        var validator = HandlerValidationFrontEnd.Build(
            candidate.Handler, candidate.ParameterTypes, options, cancellationToken);

        if (validator is null) {
            return new Resolved(candidate.Handler, null, candidate.Diagnostics);
        }

        return new Resolved(WithFilter(candidate.Handler, validator), validator, candidate.Diagnostics);
    }

    /// <summary>
    /// The same handler, carrying the filter that runs its parameters validator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>ValidationFilterProvider</c> rather than a <c>ValidateAttribute</c>, because the type
    /// being validated is the handler's nested <c>Parameters</c> class rather than anything a
    /// consumer writes against. Both resolve their validators from the container; the validator for
    /// this one is registered by the same generator run that emits the provider.
    /// </para>
    /// <para>
    /// It lands in the handler's metadata array, which is <c>object[]</c> filtered for
    /// <c>IRequestFilterProvider</c>, so an ordinary object creation is what belongs here - an
    /// attribute would need its arguments to be compile-time constants.
    /// </para>
    /// </remarks>
    private static RequestHandlerModel WithFilter(RequestHandlerModel handler, ValidatedTypeModel validator) {
        var filters = new List<AttributeModel>(handler.Filters) {
            new(
                new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition,
                    "Hardened.Requests.Runtime.Validation",
                    "ValidationFilterProvider",
                    new[] { InvokeClassGenerator.GenericParameters }),
                "",
                "")
        };

        return new RequestHandlerModel(
            handler.Name,
            handler.ControllerType,
            handler.HandlerMethod,
            handler.InvokeHandlerType,
            handler.RequestParameterInformationList,
            handler.ResponseInformation,
            filters) {
            ParametersInterface = handler.ParametersInterface,
            ParametersValidator = TypeDefinition.Get(validator.Namespace, validator.ValidatorName),
        };
    }

    /// <summary>
    /// Warnings for constraints written where this generator does not read them.
    /// </summary>
    private static ImmutableArray<Diagnostic> UncompiledConstraints(
        GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration) {

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        foreach (var parameter in methodDeclaration.ParameterList.Parameters) {
            if (context.SemanticModel.GetDeclaredSymbol(parameter) is not { } symbol) {
                continue;
            }

            foreach (var attribute in symbol.GetAttributes()) {
                if (!ConstraintAttributeFacts.IsConstraint(attribute)) {
                    continue;
                }

                diagnostics.Add(Diagnostic.Create(
                    HandlerValidationDiagnostics.ConstraintOnParameter,
                    parameter.GetLocation(),
                    attribute.AttributeClass!.Name,
                    symbol.Name));
            }
        }

        return diagnostics.ToImmutable();
    }

    private sealed record Candidate(
        RequestHandlerModel Handler,
        ImmutableArray<ITypeSymbol?> ParameterTypes,
        ImmutableArray<Diagnostic> Diagnostics);

    public sealed record Resolved(
        RequestHandlerModel Handler,
        ValidatedTypeModel? Validator,
        ImmutableArray<Diagnostic> Diagnostics);

    /// <summary>
    /// Value equality for the resolved model, so an edit elsewhere in the file does not re-run
    /// every downstream stage.
    /// </summary>
    /// <remarks>
    /// <c>RequestHandlerModel</c> has its own comparer and <c>ValidatedTypeModel</c> is a record of
    /// equatable arrays. Diagnostics are compared on what they say rather than by reference,
    /// because they are rebuilt on every keystroke.
    /// </remarks>
    public sealed class ResolvedComparer : IEqualityComparer<Resolved> {
        public static readonly ResolvedComparer Instance = new();

        private static readonly RequestHandlerModelComparer Handlers = new();

        public bool Equals(Resolved? x, Resolved? y) {
            if (ReferenceEquals(x, y)) {
                return true;
            }

            if (x is null || y is null) {
                return false;
            }

            return Handlers.Equals(x.Handler, y.Handler) &&
                Equals(x.Validator, y.Validator) &&
                x.Diagnostics.Select(Describe).SequenceEqual(y.Diagnostics.Select(Describe));
        }

        public int GetHashCode(Resolved obj) {
            unchecked {
                var hashCode = Handlers.GetHashCode(obj.Handler);

                hashCode = (hashCode * 397) ^ (obj.Validator?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ obj.Diagnostics.Length;

                return hashCode;
            }
        }

        private static string Describe(Diagnostic diagnostic) =>
            diagnostic.Id + "|" + diagnostic.Location.GetLineSpan() + "|" + diagnostic.GetMessage();
    }
}
