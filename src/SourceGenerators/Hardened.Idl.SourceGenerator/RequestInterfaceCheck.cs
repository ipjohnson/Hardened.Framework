using System.Collections.Immutable;
using System.Linq;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Hardened.Idl.SourceGenerator;

/// <summary>
/// Whether each interface a description's <c>x-hardened-request-interfaces</c> names fits the
/// <c>Parameters</c> class generated for the operation.
/// </summary>
/// <remarks>
/// <para>
/// Made here rather than in the build task, because the interfaces are written by hand in the
/// project or a library it references, and only the compilation can see their members.
/// </para>
/// <para>
/// A generated property satisfies an interface property when it has the member's exact name, which
/// is the parameter's name as <c>NamingHelper.ToParameterName</c> writes it, binds to
/// the same type, and agrees on nullability in the direction each accessor needs: a nullable
/// property cannot implement a getter that promises a value, and a non-nullable one cannot
/// implement a setter that accepts null. Those two are only warnings from the compiler, so a
/// request that does not fit would build locally and fail CI; checking them here fails both.
/// </para>
/// <para>
/// An interface that does not fit is reported and left off the base list. Added anyway, the
/// compiler would report a CS0535 for the same member beside the diagnostic, and that error does
/// not name the operation.
/// </para>
/// </remarks>
internal static class RequestInterfaceCheck
{
    public const string DiagnosticId = "HOAG033";

    // Built per call rather than held in a static field, for the RS2008 reason
    // HandlerBindingDiagnostics gives.
    private static DiagnosticDescriptor Descriptor() =>
        new(
            DiagnosticId,
            "An operation's request does not fit an interface the description names",
            "{0}",
            "Hardened.OpenApi",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    /// <summary>
    /// One verdict for every handler whose description names an interface, and nothing for the
    /// others.
    /// </summary>
    /// <remarks>
    /// Reduced to strings, so the value compares equal across compilations that change nothing it
    /// read and the handlers are not emitted again on every keystroke.
    /// </remarks>
    public static Verdicts Read(
        ImmutableArray<RequestHandlerModel> handlers,
        Compilation compilation,
        CancellationToken cancellationToken
    )
    {
        var verdicts = new List<Verdict>();
        SemanticModel? semanticModel = null;

        foreach (var handler in handlers)
        {
            if (handler.RequestInterfaceNames.Count == 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (semanticModel == null && compilation.SyntaxTrees.FirstOrDefault() is { } tree)
            {
                semanticModel = compilation.GetSemanticModel(tree);
            }

            verdicts.Add(Judge(handler, compilation, semanticModel));
        }

        return new Verdicts(verdicts);
    }

    public static void Report(SourceProductionContext context, Verdicts verdicts)
    {
        foreach (var verdict in verdicts.Items)
        {
            foreach (var problem in verdict.Problems)
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor(), Location.None, problem));
            }
        }
    }

    /// <summary>
    /// The handler as it is emitted: a copy carrying the interfaces that fit, or the handler itself
    /// when its description names none.
    /// </summary>
    public static RequestHandlerModel Apply(RequestHandlerModel handler, Verdicts verdicts)
    {
        if (handler.RequestInterfaceNames.Count == 0)
        {
            return handler;
        }

        var key = Key(handler);
        var verdict = verdicts.Items.FirstOrDefault(v => v.Handler == key);

        if (verdict == null || verdict.Implemented.Count == 0)
        {
            return handler;
        }

        var emitted = handler.WithFilters(handler.Filters);

        emitted.RequestInterfaces = verdict
            .Implemented.Select(name =>
            {
                var dot = name.LastIndexOf('.');

                return dot < 0
                    ? TypeDefinition.Get("", name)
                    : TypeDefinition.Get(name.Substring(0, dot), name.Substring(dot + 1));
            })
            .ToList();

        return emitted;
    }

    private static Verdict Judge(
        RequestHandlerModel handler,
        Compilation compilation,
        SemanticModel? semanticModel
    )
    {
        var operation = handler.OperationId ?? handler.HandlerMethod;
        var implemented = new List<string>();
        var problems = new List<string>();

        var validationInterface = handler.ParametersInterface is { } validation
            ? Written(validation, TypeOutputMode.FullName)
            : null;

        foreach (var name in handler.RequestInterfaceNames)
        {
            if (name == validationInterface || implemented.Contains(name))
            {
                continue;
            }

            var symbol = compilation.GetTypeByMetadataName(name);

            if (symbol == null)
            {
                problems.Add(
                    $"'{operation}' names {name} in x-hardened-request-interfaces, and this "
                        + "compilation declares no type of that name. Write the interface in this "
                        + "project or a library it references, and give its fully qualified name."
                );

                continue;
            }

            if (symbol.TypeKind != TypeKind.Interface)
            {
                problems.Add(
                    $"'{operation}' names {name} in x-hardened-request-interfaces, which is a "
                        + $"{symbol.TypeKind.ToString().ToLowerInvariant()} rather than an interface."
                );

                continue;
            }

            var before = problems.Count;

            foreach (var member in Members(symbol))
            {
                if (Misfit(operation, name, member, handler, semanticModel) is { } problem)
                {
                    problems.Add(problem);
                }
            }

            if (problems.Count == before)
            {
                implemented.Add(name);
            }
        }

        return new Verdict(Key(handler), implemented, problems);
    }

    /// <summary>
    /// Every member a class implementing the interface has to supply, its base interfaces'
    /// included. A member with a default implementation needs nothing.
    /// </summary>
    private static IEnumerable<ISymbol> Members(INamedTypeSymbol symbol) =>
        new[] { symbol }
            .Concat(symbol.AllInterfaces)
            .SelectMany(declaring => declaring.GetMembers())
            .Where(member =>
                member.IsAbstract
                && member
                    is not IMethodSymbol
                    {
                        MethodKind: MethodKind.PropertyGet
                            or MethodKind.PropertySet
                            or MethodKind.EventAdd
                            or MethodKind.EventRemove
                    }
            );

    private static string? Misfit(
        string operation,
        string interfaceName,
        ISymbol member,
        RequestHandlerModel handler,
        SemanticModel? semanticModel
    )
    {
        var prefix =
            $"'{operation}' names {interfaceName} in x-hardened-request-interfaces, and its "
            + $"request does not supply the member '{member.Name}'";

        if (member is not IPropertySymbol property || property.IsIndexer || property.IsStatic)
        {
            return prefix
                + ". A generated request implements an interface's instance properties and "
                + "nothing else.";
        }

        var parameter = handler.RequestParameterInformationList.FirstOrDefault(p =>
            string.Equals(p.MemberName, property.Name, StringComparison.Ordinal)
        );

        if (parameter == null)
        {
            var differentCase = handler.RequestParameterInformationList.FirstOrDefault(p =>
                string.Equals(p.MemberName, property.Name, StringComparison.OrdinalIgnoreCase)
            );

            return differentCase == null
                ? prefix + ": no parameter of the operation has that name."
                : prefix
                    + $": the request's property is named '{differentCase.MemberName}'. A member "
                    + "is matched by its exact name, so name it as the property is named.";
        }

        var generated = Written(parameter.ParameterType, TypeOutputMode.Global);
        var declared = property.Type.ToDisplayString(TypeFormat);

        if (!SameType(parameter.ParameterType, generated, property.Type, declared, semanticModel))
        {
            return prefix
                + $": the request's property is {generated}, and the member is {declared}.";
        }

        if (property.Type.IsReferenceType)
        {
            var generatedNullable = parameter.ParameterType.IsNullable;
            var annotation = property.Type.NullableAnnotation;

            if (
                property.GetMethod != null
                && generatedNullable
                && annotation == NullableAnnotation.NotAnnotated
            )
            {
                return prefix
                    + $": the request's property is {generated}, which can be null, and the "
                    + $"member is {declared}. Make the parameter required, or the member nullable.";
            }

            if (
                property.SetMethod != null
                && !generatedNullable
                && annotation == NullableAnnotation.Annotated
            )
            {
                return prefix
                    + $": the member is {declared} with a setter that accepts null, and the "
                    + $"request's property is {generated}.";
            }
        }

        if (property.SetMethod is { IsInitOnly: true })
        {
            return prefix + ": the member has an init accessor, and the request's property a set.";
        }

        return null;
    }

    /// <summary>
    /// Whether the generated property's type is the member's type, nullability aside.
    /// </summary>
    /// <remarks>
    /// The generated type is written out and bound back through the compilation, so two spellings
    /// of one type - <c>byte[]</c> and <c>global::System.Byte[]</c> - compare equal. The written
    /// forms are compared only where the type does not bind.
    /// </remarks>
    private static bool SameType(
        ITypeDefinition generatedType,
        string generated,
        ITypeSymbol declaredType,
        string declared,
        SemanticModel? semanticModel
    )
    {
        var bound = semanticModel
            ?.GetSpeculativeTypeInfo(
                0,
                SyntaxFactory.ParseTypeName(generated),
                SpeculativeBindingOption.BindAsTypeOrNamespace
            )
            .Type;

        if (bound != null && bound.TypeKind != TypeKind.Error)
        {
            return SymbolEqualityComparer.Default.Equals(bound, declaredType);
        }

        return string.Equals(
            Written(generatedType.MakeNullable(false), TypeOutputMode.Global),
            declaredType
                .WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(TypeFormat),
            StringComparison.Ordinal
        );
    }

    private static string Written(ITypeDefinition type, TypeOutputMode mode)
    {
        var builder = new StringBuilder();

        type.WriteTypeName(builder, mode);

        return builder.ToString();
    }

    private static string Key(RequestHandlerModel handler) =>
        Written(handler.InvokeHandlerType, TypeOutputMode.Global);

    /// <summary>What one handler's description names, sorted into what fits and what does not.</summary>
    public sealed class Verdict : IEquatable<Verdict>
    {
        public Verdict(
            string handler,
            IReadOnlyList<string> implemented,
            IReadOnlyList<string> problems
        )
        {
            Handler = handler;
            Implemented = implemented;
            Problems = problems;
        }

        public string Handler { get; }

        public IReadOnlyList<string> Implemented { get; }

        public IReadOnlyList<string> Problems { get; }

        public bool Equals(Verdict? other) =>
            other != null
            && Handler == other.Handler
            && Implemented.SequenceEqual(other.Implemented)
            && Problems.SequenceEqual(other.Problems);

        public override bool Equals(object? obj) => Equals(obj as Verdict);

        public override int GetHashCode() => Handler.GetHashCode();
    }

    /// <summary>Every handler's verdict, compared by value.</summary>
    public sealed class Verdicts : IEquatable<Verdicts>
    {
        public Verdicts(IReadOnlyList<Verdict> items)
        {
            Items = items;
        }

        public IReadOnlyList<Verdict> Items { get; }

        public bool Equals(Verdicts? other) => other != null && Items.SequenceEqual(other.Items);

        public override bool Equals(object? obj) => Equals(obj as Verdicts);

        public override int GetHashCode() => Items.Count;
    }
}
