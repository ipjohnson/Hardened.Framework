using System.Collections.Immutable;
using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ValidationModules.SourceGenerator.Impl;
using ValidationModules.SourceGenerator.Impl.FrontEnds;
using ValidationModules.SourceGenerator.Impl.Models;

namespace Hardened.SourceGenerator.Validation;

/// <summary>
/// Builds the validator model for a hand-written handler's generated <c>Parameters</c> class.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Constraints on a body model are read, a validator is emitted for
/// it and registered - all of that already works, for a hand-written controller exactly as for a
/// spec-driven one, because <c>ValidationModules.SourceGenerator</c> scans every type in the
/// compilation and every rules class. What was missing was the last step: nothing attached a
/// filter, so the validator was generated, registered, and never invoked. A request carrying a body
/// that violated its own declared constraints was answered normally.
/// </para>
/// <para>
/// <b>Why it cannot be done by ValidationModules' generator.</b> The value that gets validated at run
/// time is reached through the handler's nested <c>Parameters</c> class, and that class is emitted
/// by this generator. Roslyn generators do not see each other's regular output, so the validation
/// generator cannot emit a validator for a type it cannot observe. Moving <c>Parameters</c> into
/// post-initialization output does not help either, because post-init runs before the semantic
/// model that reading the handler's signature requires. The spec path escapes this only because an
/// MSBuild task runs before compilation and writes its interfaces as ordinary source.
/// </para>
/// <para>
/// So the validator for <c>Parameters</c> is emitted here, where the type is known, and it does two
/// things. It descends into the parameters that carry structure, calling the validators
/// ValidationModules emitted for their types - named by convention rather than observed, which
/// is safe only because naming one that does not exist fails to compile. And it checks the
/// constraints written on the parameters themselves: <c>[Range]</c> on a query value,
/// <c>[StringLength]</c> on a header, <c>[Required]</c> on a path token. Those are read and
/// resolved by ValidationModules' own front end, through the two entry points it exposes for any
/// member symbol, so a parameter is held to exactly the policy a property is - both vocabularies,
/// the pattern forms, the type-fit diagnostics - and nothing about a constraint is decided here.
/// </para>
/// </remarks>
public static class HandlerValidationFrontEnd
{
    /// <summary>
    /// What <see cref="Build"/> produces: the model, or null when nothing about the handler asks
    /// for one, and whatever was reported reading the parameters' own constraints.
    /// </summary>
    public sealed record Built(ValidatedTypeModel? Model, ImmutableArray<Diagnostic> Diagnostics);

    /// <summary>
    /// The model for this handler's parameters validator, with the diagnostics its constraints
    /// raised.
    /// </summary>
    /// <param name="handler">The handler, whose parameter list this walks.</param>
    /// <param name="parameters">
    /// The symbol of each parameter, positionally aligned with the handler's parameter list. Passed
    /// in rather than resolved here because the decision below depends on build properties, which
    /// are not reachable from inside a syntax transform - so the symbols have to survive one
    /// pipeline stage to meet them.
    /// </param>
    /// <param name="compilation">
    /// The compilation the parameter symbols came from - the front end walks inherited members and
    /// answers accessibility questions against it, so it must be the same snapshot.
    /// </param>
    public static Built Build(
        RequestHandlerModel handler,
        ImmutableArray<IParameterSymbol?> parameters,
        Compilation compilation,
        ValidationGeneratorOptions options,
        EquatableArray<string> rulesTargets,
        CancellationToken cancellationToken
    )
    {
        // The spec path already attached its own filter, against an interface the build task named.
        // Emitting a second validator here would validate the same values twice and report every
        // failure twice with it.
        if (handler.ParametersInterface != null)
        {
            return new Built(null, ImmutableArray<Diagnostic>.Empty);
        }

        // [ValidateNever] on the handler leaves every parameter unvalidated, so no validator.
        if (
            parameters.FirstOrDefault(symbol => symbol != null)?.ContainingSymbol
                is IMethodSymbol method
            && ValidatesNever(method)
        )
        {
            return new Built(null, ImmutableArray<Diagnostic>.Empty);
        }

        // One front end for the handler, and kept: what it reports about a constraint written on a
        // parameter is reported nowhere else. HasValidator's is thrown away for the opposite reason.
        var frontEnd = new AttributeFrontEnd(
            compilation,
            options.CompileDataAnnotations,
            options.FieldNamer,
            options.ResolvedPatternPolicy
        );

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var properties = ImmutableArray.CreateBuilder<ValidatedPropertyModel>();
        var described = new HashSet<string>(rulesTargets, StringComparer.Ordinal);

        bool HasRulesClass(INamedTypeSymbol type) => described.Contains(RulesTargets.Key(type));

        for (var i = 0; i < handler.RequestParameterInformationList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parameter = handler.RequestParameterInformationList[i];

            if (!CarriesRequestData(parameter.BindingType) || i >= parameters.Length)
            {
                continue;
            }

            if (parameters[i] is not { } symbol || ValidatesNever(symbol))
            {
                continue;
            }

            if (
                BuildProperty(
                    symbol,
                    parameter,
                    compilation,
                    options,
                    frontEnd,
                    HasRulesClass,
                    diagnostics
                ) is
                { } property
            )
            {
                properties.Add(property);
            }
        }

        diagnostics.AddRange(frontEnd.Diagnostics);

        if (properties.Count == 0)
        {
            return new Built(null, diagnostics.ToImmutable());
        }

        return new Built(
            new ValidatedTypeModel(
                handler.InvokeHandlerType.Namespace,
                "Parameters",
                $"global::{handler.InvokeHandlerType.Namespace}.{handler.InvokeHandlerType.Name}.Parameters",
                ValidatorNameFor(handler),
                new EquatableArray<ValidatedPropertyModel>(properties.ToImmutable())
            ),
            diagnostics.ToImmutable()
        );
    }

    /// <summary>
    /// The validator class emitted for a handler's parameters.
    /// </summary>
    /// <remarks>
    /// Named off the handler type, which already carries a suffix computed from the signature, so
    /// two overloads reaching the same route do not collide.
    /// </remarks>
    public static string ValidatorNameFor(RequestHandlerModel handler) =>
        handler.InvokeHandlerType.Name + "ParametersValidator";

    /// <summary>
    /// Whether <paramref name="symbol"/> carries <c>[ValidateNever]</c>.
    /// </summary>
    private static bool ValidatesNever(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (
                attribute.AttributeClass is { Name: "ValidateNeverAttribute" } type
                && type.ContainingNamespace?.ToDisplayString()
                    == "Hardened.Requests.Runtime.Validation"
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a parameter holds something the caller sent, as opposed to something the container
    /// or the pipeline supplied.
    /// </summary>
    private static bool CarriesRequestData(ParameterBindType bindingType) =>
        bindingType
            is ParameterBindType.Body
                or ParameterBindType.Path
                or ParameterBindType.QueryString
                or ParameterBindType.Header
                or ParameterBindType.Cookie
                or ParameterBindType.Form
                or ParameterBindType.CustomAttribute;

    /// <summary>
    /// The symbol of every parameter, aligned with the handler's parameter list.
    /// </summary>
    public static ImmutableArray<IParameterSymbol?> ParameterSymbolsOf(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration
    )
    {
        var symbols = ImmutableArray.CreateBuilder<IParameterSymbol?>(
            methodDeclaration.ParameterList.Parameters.Count
        );

        foreach (var parameter in methodDeclaration.ParameterList.Parameters)
        {
            symbols.Add(context.SemanticModel.GetDeclaredSymbol(parameter));
        }

        return symbols.ToImmutable();
    }

    /// <summary>
    /// One parameter, described as a property of the <c>Parameters</c> class it becomes: whether
    /// its type has a validator of its own to descend into, and what it constrains itself.
    /// </summary>
    private static ValidatedPropertyModel? BuildProperty(
        IParameterSymbol symbol,
        RequestParameterInformation parameter,
        Compilation compilation,
        ValidationGeneratorOptions options,
        AttributeFrontEnd frontEnd,
        Func<INamedTypeSymbol, bool> hasRulesClass,
        ImmutableArray<Diagnostic>.Builder diagnostics
    )
    {
        var type = symbol.Type;
        var shape = PropertyShape.Scalar;
        string? elementTypeName = null;
        string? elementValidatorName = null;

        var dictionary = TypeFacts.DictionaryTypesOf(type);
        var elementType = TypeFacts.ElementTypeOf(type);

        if (
            dictionary is { } entry
            && HasValidator(entry.Value, compilation, options, hasRulesClass)
        )
        {
            shape = PropertyShape.Dictionary;
            elementTypeName = Qualified(entry.Value);
            elementValidatorName = QualifiedValidator((INamedTypeSymbol)entry.Value);
        }
        else if (
            elementType is not null
            && HasValidator(elementType, compilation, options, hasRulesClass)
        )
        {
            shape = PropertyShape.Collection;
            elementTypeName = Qualified(elementType);
            elementValidatorName = QualifiedValidator((INamedTypeSymbol)elementType);
        }
        else if (
            dictionary is null
            && elementType is null
            && HasValidator(type, compilation, options, hasRulesClass)
        )
        {
            shape = PropertyShape.Object;
            elementValidatorName = QualifiedValidator((INamedTypeSymbol)type);
        }

        var descends = shape != PropertyShape.Scalar;
        var constraints = Constraints(symbol, type, elementType, frontEnd, diagnostics);

        if (!descends && constraints.Count == 0)
        {
            return null;
        }

        return new ValidatedPropertyModel(
            parameter.Name,
            FieldNameFor(parameter),
            Qualified(type),
            shape,
            elementTypeName,
            elementValidatorName,
            type.IsReferenceType,
            type.SpecialType == SpecialType.System_String,
            TypeFacts.IsNullableValueType(type),
            elementType is not null && TypeFacts.IsIndexable(type),
            TypeFacts.CountAccessor(type),
            descends,
            new EquatableArray<ConstraintModel>(constraints.ToImmutableArray()),
            DisplayName: constraints.Count > 0 ? symbol.Name : null
        );
    }

    /// <summary>
    /// The constraints written on the parameter itself, read and resolved by ValidationModules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReadConstraintsFor</c> and <c>ValidateAndResolve</c> are the same two calls the front
    /// end's own <c>Build</c> makes for a property, typed on the member symbol rather than on a
    /// property, so the reading is not reimplemented here and cannot drift from it.
    /// </para>
    /// <para>
    /// A <c>When</c> or <c>Unless</c> names a member of the model the constraint sits on, and a
    /// handler parameter sits on no model - the front end would look for the member on a type it
    /// was never given. Refused here, before the reader, as the one thing about a parameter's
    /// constraint this generator decides itself.
    /// </para>
    /// </remarks>
    private static List<ConstraintModel> Constraints(
        IParameterSymbol symbol,
        ITypeSymbol type,
        ITypeSymbol? elementType,
        AttributeFrontEnd frontEnd,
        ImmutableArray<Diagnostic>.Builder diagnostics
    )
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!ConstraintAttributeFacts.IsConstraint(attribute))
            {
                continue;
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key is not ("When" or "Unless"))
                {
                    continue;
                }

                diagnostics.Add(
                    Diagnostic.Create(
                        HandlerValidationDiagnostics.ConditionOnParameterConstraint,
                        symbol.Locations.FirstOrDefault(),
                        argument.Key,
                        attribute.AttributeClass!.Name,
                        symbol.Name
                    )
                );

                return new List<ConstraintModel>();
            }
        }

        var constraints = frontEnd.ReadConstraintsFor(symbol, type);

        if (constraints.Count > 0)
        {
            frontEnd.ValidateAndResolve(
                symbol,
                type,
                constraints,
                type.SpecialType == SpecialType.System_String,
                elementType
            );
        }

        return constraints;
    }

    /// <summary>
    /// Whether <c>ValidationModules.SourceGenerator</c> will emit a validator for this type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A type a rules class describes always gets one, and a type from a library that was built
    /// with its validator already has one. Otherwise the answer comes from running
    /// ValidationModules' own attribute front end and looking at whether it produced a model, told
    /// which types have rules classes the way ValidationModules' generator tells it, so a member
    /// that descends into a rules-described type counts. Anything short of that is an approximation
    /// of a decision another generator is making, and the two failures it produces are a
    /// compilation that names a validator nobody emitted, or a body whose constraints are silently
    /// never checked. Attributes alone were that approximation, and a model whose rules lived in a
    /// rules class was bound and never validated.
    /// </para>
    /// <para>
    /// Its diagnostics are discarded here. This is a question, not a reading - the validation
    /// generator builds the same model for the same type and reports them there, and reporting them
    /// from both places would double every one of them.
    /// </para>
    /// </remarks>
    private static bool HasValidator(
        ITypeSymbol type,
        Compilation compilation,
        ValidationGeneratorOptions options,
        Func<INamedTypeSymbol, bool> hasRulesClass
    )
    {
        if (type is not INamedTypeSymbol named || named.SpecialType != SpecialType.None)
        {
            return false;
        }

        if (hasRulesClass(named) || ArrivesWithAValidator(named, compilation))
        {
            return true;
        }

        var frontEnd = new AttributeFrontEnd(
            compilation,
            options.CompileDataAnnotations,
            options.FieldNamer,
            options.ResolvedPatternPolicy
        );

        return frontEnd.Build(
            named,
            ValidationGeneratorOptions.ValidatorNameFor,
            hasRulesClass: hasRulesClass
        )
            is not null;
    }

    /// <summary>
    /// Whether a type from a referenced assembly was compiled with a ValidationModules validator
    /// beside it.
    /// </summary>
    /// <remarks>
    /// The rules classes this compilation can see are its own. A library that keeps a model's rules
    /// class next to the model ran ValidationModules' generator when it built, and the validator it
    /// emitted is the only trace of that rules class left here. The interface is checked as well as
    /// the name, because a FluentValidation <c>PetValidator</c> beside a <c>Pet</c> is a common
    /// shape and naming it as an <c>IValidatorFor&lt;Pet&gt;</c> would not compile.
    /// </remarks>
    private static bool ArrivesWithAValidator(INamedTypeSymbol type, Compilation compilation)
    {
        if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
        {
            return false;
        }

        var name = ValidationGeneratorOptions.ValidatorNameFor(type);
        var metadataName = type.ContainingNamespace.IsGlobalNamespace
            ? name
            : $"{type.ContainingNamespace.ToDisplayString()}.{name}";

        if (
            type.ContainingAssembly.GetTypeByMetadataName(metadataName) is not { } validator
            || !compilation.IsSymbolAccessibleWithin(validator, compilation.Assembly)
        )
        {
            return false;
        }

        foreach (var contract in validator.AllInterfaces)
        {
            if (
                contract.ConstructedFrom.ToDisplayString() == KnownTypes.ValidatorForInterface
                && SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], type)
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The name errors under this parameter are pathed from.
    /// </summary>
    /// <remarks>
    /// The binding name, which is what the caller actually sent, rather than the C# parameter name
    /// put through a field namer. A body has no binding name and reports under the parameter's own
    /// name, so a failure inside it reads <c>pet.name</c> - the same shape the spec path produces
    /// as <c>body.name</c>, and for the same reason: a body field and a route parameter that share
    /// a name have to stay distinguishable.
    /// </remarks>
    private static string FieldNameFor(RequestParameterInformation parameter) =>
        string.IsNullOrEmpty(parameter.BindingName) ? parameter.Name : parameter.BindingName;

    private static string Qualified(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string QualifiedValidator(INamedTypeSymbol type) =>
        GeneratedNames.QualifiedValidator(type);
}
