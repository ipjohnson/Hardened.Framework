using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// One case of a handler's declared response set: what type it is, and what it answers with.
/// </summary>
public readonly struct UnionCaseModel {

    public UnionCaseModel(string typeName, int status, bool appliesHeaders, bool hasBody) {
        TypeName = typeName;
        Status = status;
        AppliesHeaders = appliesHeaders;
        HasBody = hasBody;
    }

    /// <summary>The case type, fully qualified with <c>global::</c>, ready to emit.</summary>
    public string TypeName { get; }

    /// <summary>The status this case answers with.</summary>
    public int Status { get; }

    /// <summary>Whether the case contributes headers through <c>IProvidesResponseHeaders</c>.</summary>
    public bool AppliesHeaders { get; }

    /// <summary>Whether anything is serialized for this case.</summary>
    public bool HasBody { get; }
}

/// <summary>
/// Recognises a handler's return type as a declared response set, and says what each case answers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Matched structurally, never by name and never by attribute</b> - a public single-parameter
/// constructor per case plus a public <c>object?</c> <c>Value</c> getter. That is the C# basic union
/// pattern, so one check recognises <c>Response&lt;T1..Tn&gt;</c>, a generated response union and a
/// C# 15 <c>union</c> declaration alike. The generator never asks which, never asks what language
/// version hosts it, and stays on <c>netstandard2.0</c>.
/// </para>
/// <para>
/// <b>Takes a <c>SemanticModel</c> rather than a <c>GeneratorSyntaxContext</c>.</b> The context is a
/// wrapper over a node and a model, only the model is used, and it cannot be constructed outside a
/// running generator - so taking it would mean this could only ever be exercised through a whole
/// generator run. Every generator here compiles these sources in rather than referencing them, so
/// "tested through the web generator" is a statement about a different compilation of this file.
/// </para>
/// <para>
/// <b>It also never asks what mode the module declared.</b> Code-first, the return type has already
/// decided: if it matches, union glue is emitted, and if it does not, the existing path runs.
/// <c>[ResponseModel]</c> is a declaration of intent for an analyzer to check methods against, not
/// an input to this - which is what makes every code path work regardless of what the module says.
/// </para>
/// </remarks>
public static class UnionResponseSelector {

    private const string ValuePropertyName = "Value";

    private const string HttpStatusAttributeName = "HttpStatusAttribute";

    private const string HeaderInterfaceName = "IProvidesResponseHeaders";

    private const string ResponsesNamespace = "Hardened.Requests.Abstract.Responses";

    /// <summary>
    /// The cases of the handler's return type, encoded, or null where it is not a response set.
    /// </summary>
    /// <param name="semanticModel">The model the return type is resolved against.</param>
    /// <param name="methodDeclaration">The handler.</param>
    /// <param name="successStatus">
    /// The endpoint's success status, which every case that does not name one of its own takes.
    /// </param>
    public static string? Read(
        SemanticModel semanticModel, MethodDeclarationSyntax methodDeclaration, int? successStatus) {
        var returned = Unwrap(semanticModel.GetTypeInfo(methodDeclaration.ReturnType).Type);

        if (returned == null) {
            return null;
        }

        var cases = Cases(returned, successStatus ?? 200);

        return cases == null ? null : Encode(cases);
    }

    /// <summary>
    /// What is wrong with the declared case set, encoded, or null where nothing is.
    /// </summary>
    /// <remarks>
    /// Found here, where the symbols still exist, and reported from the routing generator, where a
    /// <c>SourceProductionContext</c> does. A syntax transform cannot report a diagnostic, which is
    /// the same reason a stream framing attribute on a handler that streams nothing is carried
    /// forward rather than rejected in place.
    /// </remarks>
    public static string? Diagnose(
        SemanticModel semanticModel, MethodDeclarationSyntax methodDeclaration, int? successStatus) {
        var returned = Unwrap(semanticModel.GetTypeInfo(methodDeclaration.ReturnType).Type);

        if (returned == null) {
            return null;
        }

        var symbols = CaseSymbols(returned);

        if (symbols.Count == 0) {
            return null;
        }

        foreach (var symbol in symbols) {
            if (symbol.SpecialType == SpecialType.System_Object ||
                symbol.TypeKind == TypeKind.Dynamic) {
                return UntypedFinding + FieldSeparator + Display(symbol);
            }
        }

        // Pairwise over a closed set that is never large. Only across statuses: two cases of one
        // status share a oneOf and their relationship decides nothing.
        for (var i = 0; i < symbols.Count; i++) {
            for (var j = i + 1; j < symbols.Count; j++) {
                var first = symbols[i];
                var second = symbols[j];

                var firstStatus = Status(first, successStatus ?? 200);
                var secondStatus = Status(second, successStatus ?? 200);

                if (firstStatus == secondStatus) {
                    continue;
                }

                if (!Assignable(first, second) && !Assignable(second, first)) {
                    continue;
                }

                return AssignableFinding + FieldSeparator + Display(first) +
                       FieldSeparator + firstStatus + FieldSeparator + Display(second) +
                       FieldSeparator + secondStatus;
            }
        }

        return null;
    }

    /// <summary>The finding kinds, which the reporter switches on.</summary>
    public const string UntypedFinding = "untyped";

    public const string AssignableFinding = "assignable";

    /// <summary>The fields of an encoded finding, in the order above.</summary>
    public static IReadOnlyList<string> DecodeFinding(string finding) => finding.Split(FieldSeparator);

    private static string Display(ITypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static bool Assignable(ITypeSymbol from, ITypeSymbol to) {
        for (var current = from.BaseType; current != null; current = current.BaseType) {
            if (SymbolEqualityComparer.Default.Equals(current, to)) {
                return true;
            }
        }

        foreach (var contract in from.AllInterfaces) {
            if (SymbolEqualityComparer.Default.Equals(contract, to)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>The case types of a response set, or nothing where the type is not one.</summary>
    private static IReadOnlyList<ITypeSymbol> CaseSymbols(INamedTypeSymbol type) {
        var value = type
            .GetMembers(ValuePropertyName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                !p.IsStatic &&
                p.Type.SpecialType == SpecialType.System_Object);

        if (value == null) {
            return Array.Empty<ITypeSymbol>();
        }

        return type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 1)
            .Select(c => c.Parameters[0].Type)
            .ToList();
    }

    /// <summary>
    /// Past <c>Task&lt;T&gt;</c> and <c>ValueTask&lt;T&gt;</c> to the type a handler actually
    /// declares.
    /// </summary>
    /// <remarks>
    /// One level only. <c>Task&lt;Task&lt;T&gt;&gt;</c> is not something a handler returns, and
    /// unwrapping repeatedly would turn a genuine <c>Task</c>-shaped case type into its argument.
    /// </remarks>
    private static INamedTypeSymbol? Unwrap(ITypeSymbol? returnType) {
        if (returnType is not INamedTypeSymbol named) {
            return null;
        }

        if (named.IsGenericType &&
            (named.Name == "Task" || named.Name == "ValueTask") &&
            named.TypeArguments.Length == 1) {
            return named.TypeArguments[0] as INamedTypeSymbol;
        }

        return named;
    }

    /// <summary>
    /// The basic union pattern check, and the case list if it holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves are required. A type with a public <c>object? Value</c> and no per-case
    /// constructor states no case set - that is an envelope, and an envelope has no type→status
    /// answer, which is the thing the whole model rests on not being possible.
    /// </para>
    /// <para>
    /// A case appearing twice is rejected rather than deduplicated. Two identical type arguments
    /// produce two identical conversions and the compiler reports CS0457 at the point of use, so a
    /// caller cannot construct one - and emitting a switch with two arms of the same type would be
    /// unreachable code hiding a contradiction the author needs to see.
    /// </para>
    /// </remarks>
    private static List<UnionCaseModel>? Cases(INamedTypeSymbol type, int successStatus) {
        var value = type
            .GetMembers(ValuePropertyName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                !p.IsStatic &&
                p.Type.SpecialType == SpecialType.System_Object);

        if (value == null) {
            return null;
        }

        var constructors = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 1)
            .ToList();

        if (constructors.Count == 0) {
            return null;
        }

        var cases = new List<UnionCaseModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var constructor in constructors) {
            var caseType = constructor.Parameters[0].Type;
            var name = caseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (!seen.Add(name)) {
                return null;
            }

            cases.Add(new UnionCaseModel(
                name,
                Status(caseType, successStatus),
                AppliesHeaders(caseType),
                HasBody(Status(caseType, successStatus))));
        }

        return cases;
    }

    /// <summary>
    /// The case's own <c>[HttpStatus]</c>, or the endpoint's success status.
    /// </summary>
    /// <remarks>
    /// Defaulting to the endpoint's success status rather than hardcoding 200 is what covers a POST
    /// that creates without annotating every case, and it avoids inferring a status from the HTTP
    /// verb - cleverness that is wrong for a large share of REST codebases. Two unannotated cases in
    /// one set is meaningful rather than an error: it is two shapes under one status, which is what
    /// a schema <c>oneOf</c> within a 200 is.
    /// </remarks>
    private static int Status(ITypeSymbol caseType, int successStatus) {
        foreach (var attribute in caseType.GetAttributes()) {
            if (attribute.AttributeClass?.Name != HttpStatusAttributeName) {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is int declared) {
                return declared;
            }
        }

        return successStatus;
    }

    /// <summary>
    /// Whether the case contributes headers, read off the interface rather than guessed per status.
    /// </summary>
    /// <remarks>
    /// Checked here so the emitted switch calls <c>ApplyHeaders</c> only where there is something to
    /// apply, rather than type-testing every response at run time. A user's own case type that
    /// implements the interface gets the same treatment as a built-in one, which is the point of it
    /// being an interface.
    /// </remarks>
    private static bool AppliesHeaders(ITypeSymbol caseType) =>
        caseType.AllInterfaces.Any(i =>
            i.Name == HeaderInterfaceName &&
            i.ContainingNamespace?.ToDisplayString() == ResponsesNamespace);

    /// <summary>
    /// Whether a status may carry a body at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From the status rather than from the type, because these two are the statuses RFC 9110 says
    /// have no body - a rule no response type can opt out of, and one some clients and
    /// intermediaries enforce by rejecting the message.
    /// </para>
    /// <para>
    /// It is deliberately narrower than <c>IHttpStatusResponse.HasBody</c>, which a type can also
    /// set for a status where a body is merely unwanted - a 202 describing work that has not
    /// happened. That is a run-time signal for the serializer to honour and is not wired here;
    /// answering it from the emitted switch would mean reading a property's expression body out of
    /// a symbol, which is not something a symbol carries.
    /// </para>
    /// </remarks>
    private static bool HasBody(int status) => status != 204 && status != 304;

    #region encoding

    // Encoded rather than carried as a list, because ResponseInformationModel is a record whose
    // synthesized equality is the incremental generator's cache key - and a List<T> member compares
    // by reference there, so two identical case sets would look different to the cache and two
    // different ones could look the same. ProducedContentTypes is a joined string in that same file
    // for exactly this reason, with the reasoning written out.
    private const char CaseSeparator = ';';

    private const char FieldSeparator = '|';

    public static string Encode(IReadOnlyList<UnionCaseModel> cases) {
        var builder = new StringBuilder();

        for (var i = 0; i < cases.Count; i++) {
            if (i > 0) {
                builder.Append(CaseSeparator);
            }

            builder.Append(cases[i].TypeName)
                .Append(FieldSeparator).Append(cases[i].Status)
                .Append(FieldSeparator).Append(cases[i].AppliesHeaders ? '1' : '0')
                .Append(cases[i].HasBody ? '1' : '0');
        }

        return builder.ToString();
    }

    public static IReadOnlyList<UnionCaseModel> Decode(string? encoded) {
        if (string.IsNullOrEmpty(encoded)) {
            return Array.Empty<UnionCaseModel>();
        }

        var cases = new List<UnionCaseModel>();

        foreach (var part in encoded!.Split(CaseSeparator)) {
            var fields = part.Split(FieldSeparator);

            if (fields.Length != 3 || fields[2].Length != 2) {
                continue;
            }

            if (!int.TryParse(fields[1], out var status)) {
                continue;
            }

            cases.Add(new UnionCaseModel(
                fields[0], status, fields[2][0] == '1', fields[2][1] == '1'));
        }

        return cases;
    }

    #endregion
}
