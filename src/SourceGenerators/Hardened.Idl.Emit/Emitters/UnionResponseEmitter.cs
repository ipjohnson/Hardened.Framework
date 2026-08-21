using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl.Models;

namespace Hardened.Idl.Emitters;

/// <summary>
/// An operation's declared responses, as a named union and one case type per status.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="ErrorResponseEmitter"/>, and it emits the same names without the
/// <c>Exception</c> suffix. That one expresses a declared error by having the implementation throw,
/// leaving the signature saying <c>Task&lt;Pet&gt;</c>; this one puts the whole response set in the
/// return type. Which of the two runs is the module's response model, and only one of them does -
/// emitting both would offer two ways to answer the same 404 and no way to tell which a handler
/// used.
/// </para>
/// <para>
/// <b>The per-status wrapper types are mandatory, not stylistic.</b> The repo's own
/// <c>DeclaredErrors</c> fixture declares 404 and 409 both <c>$ref</c>-ing <c>ApiError</c>, so the
/// naive shape is <c>Response&lt;Pet, ApiError, ApiError&gt;</c> - two identical conversions, which
/// is CS0457 at the point of use. Wrapping per status makes every case a distinct type. It also
/// buys the two things <c>Response&lt;T1..Tn&gt;</c> cannot give the specification-first path:
/// unbounded arity, since the container is generated per operation rather than shipped at fixed
/// arities, and a status that declares no body at all - a 503 with nothing to send is a case type
/// carrying nothing, which no <c>Response&lt;T&gt;</c> position can express.
/// </para>
/// <para>
/// The container's shape is <see cref="OneOfEmitter"/>'s, deliberately: a public single-parameter
/// constructor per case, a public <c>object? Value</c>, and one implicit conversion per case. That
/// is the C# basic union pattern, so the code-first selector recognises this through the same
/// structural check it uses for <c>Response&lt;T1..Tn&gt;</c> and a language <c>union</c>, and the
/// dispatch generator needs to know nothing about where the type came from.
/// </para>
/// </remarks>
internal static class UnionResponseEmitter {

    /// <summary>
    /// The union type and its case types, for every operation the service declares.
    /// </summary>
    public static IReadOnlyList<ClassDefinition> Emit(
        IConstructContainer container, ServiceModel service, string modelsNamespace,
        bool asLanguageUnion = false) {
        var emitted = new List<ClassDefinition>();

        foreach (var operation in service.Operations) {
            var cases = new List<CaseType>();

            foreach (var response in operation.ErrorResponses) {
                var caseType = EmitCaseType(container, operation, response, modelsNamespace);

                emitted.Add(caseType.Definition);
                cases.Add(caseType);
            }

            emitted.Add(
                EmitContainer(container, operation, cases, modelsNamespace, asLanguageUnion));
        }

        return emitted;
    }

    /// <summary>The name the service interface returns for this operation.</summary>
    /// <remarks>
    /// Public because <c>ServiceInterfaceEmitter</c> names it in a signature and this is the only
    /// definition of the scheme. Deriving it a second time there is how a generated type and the
    /// signature that returns it come to disagree.
    /// </remarks>
    public static string ContainerName(OperationModel operation) =>
        operation.MethodName + "Response";

    /// <summary>The case type for one declared status.</summary>
    public static string CaseName(OperationModel operation, int statusCode) =>
        operation.MethodName + StatusName(statusCode);

    /// <summary>
    /// One record per declared status, carrying the body that status declares, or nothing.
    /// </summary>
    /// <remarks>
    /// A record rather than a class, so two responses carrying equal bodies compare equal - which is
    /// what a test asserting on a handler's result wants, and what an exception could never give it.
    /// Sealed, because a case type assignable to another case type in the same set has no
    /// unambiguous match order.
    /// </remarks>
    private static CaseType EmitCaseType(
        IConstructContainer container, OperationModel operation, ErrorResponseModel response,
        string modelsNamespace) {
        var name = CaseName(operation, response.StatusCode);

        var definition = container.AddClass(name);

        definition.TypeKeyword = ClassKeyword.Record;
        definition.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;

        definition.Comment = DocComment.Format(response.Description)
            ?? $"The {response.StatusCode} response declared for {operation.HttpMethod} {operation.Path}.";

        // [HttpStatus], so the dispatch generator reads this case's status from the type rather
        // than from the specification it can no longer see. It is the same attribute a hand-written
        // response type carries, which is what keeps one status resolution serving both front ends.
        definition.AddAttribute(
            TypeDefinition.Get("Hardened.Requests.Abstract.Responses", "HttpStatusAttribute"),
            new CodeOutputComponent(
                response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)) {
                Indented = false
            });

        if (response.Ref == null) {
            return new CaseType(definition, name, hasBody: false);
        }

        var payload = TypeDefinition.Get(
            modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(response.Ref)));

        var constructor = definition.AddConstructor();

        // Primary, so Body is a property of the record rather than a constructor parameter that is
        // thrown away. An ordinary constructor with an empty body compiles and silently discards
        // the payload, which is the shape a handler would then find empty at run time.
        constructor.IsPrimary = true;
        constructor.Modifiers |= ComponentModifier.Public;
        constructor.AddParameter(payload, "Body");

        return new CaseType(definition, name, hasBody: true);
    }

    /// <summary>
    /// The named union the operation returns.
    /// </summary>
    /// <remarks>
    /// The success case is the operation's own response type rather than a wrapper, so a handler
    /// returns the pet it already had. Only the declared non-2xx statuses need wrapping, because
    /// only they can collide with each other on a shared schema.
    /// </remarks>
    private static ClassDefinition EmitContainer(
        IConstructContainer container, OperationModel operation, IReadOnlyList<CaseType> cases,
        string modelsNamespace, bool asLanguageUnion) {
        var name = ContainerName(operation);

        var branchTypes = new List<ITypeDefinition>();

        var success = SuccessBranchType(operation, modelsNamespace);

        if (success != null) {
            branchTypes.Add(success);
        }

        foreach (var caseType in cases) {
            branchTypes.Add(TypeDefinition.Get(modelsNamespace, caseType.Name));
        }

        var branches = new List<string>();

        foreach (var branchType in branchTypes) {
            branches.Add(branchType.Namespace + "." + branchType.Name);
        }

        var comment = $"Every response {operation.HttpMethod} {operation.Path} declares.";

        var type = container.AddClass(name);

        type.Modifiers |= ComponentModifier.Public;
        type.Comment = comment;

        // The one declaration that differs between the two union modes, and the reason C.7 is
        // small: the keyword and the struct compile to the same shape - a constructor and an
        // implicit conversion per case plus a public object? Value - so the per-status case types
        // above are emitted byte-identical and the code-first selector recognises either through the
        // same structural check. Moving between the modes rewrites no handler. What can break is
        // code pattern-matching on the wrapper, because patterns on a language union unwrap to
        // Value, and that is why the modes are named rather than inferred.
        if (asLanguageUnion) {
            type.TypeKeyword = ClassKeyword.Union;

            foreach (var branchType in branchTypes) {
                type.AddUnionCase(branchType);
            }

            return type;
        }

        type.TypeKeyword = ClassKeyword.Struct;

        var value = type.AddProperty(TypeDefinition.Get(typeof(object)).MakeNullable(), "Value");

        value.Modifiers |= ComponentModifier.Public;
        value.Comment = "The case this response holds.";
        value.Set = null;

        // Constructors and conversions from one list, for the reason OneOfEmitter gives: a branch is
        // a qualified name rather than a type this assembly can reference, and taking both from the
        // same list is what keeps a constructor and its conversion from disagreeing about the set.
        foreach (var branch in branches) {
            type.AddComponent(
                new CodeOutputComponent($"public {name}({branch} value) => Value = value;") {
                    Indented = true
                });
        }

        foreach (var branch in branches) {
            type.AddComponent(
                new CodeOutputComponent(
                    $"public static implicit operator {name}({branch} value) => new(value);") {
                    Indented = true
                });
        }

        var toString = type.AddMethod("ToString");

        toString.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        toString.SetReturnType(TypeDefinition.Get(typeof(string)));
        toString.AddIndentedStatement("return Value?.ToString() ?? \"\"");

        return type;
    }

    /// <summary>
    /// The operation's success payload as a branch, or null where it answers with no body.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than <c>ServiceInterfaceEmitter.GetReturnType</c>. A streamed or
    /// raw-bytes response is not a response set - the first is many bodies and the second is one the
    /// application already holds encoded - and neither belongs in a union of statuses.
    /// </remarks>
    private static ITypeDefinition? SuccessBranchType(
        OperationModel operation, string modelsNamespace) {
        if (operation.ResponseRef != null) {
            return TypeDefinition.Get(
                modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseRef)));
        }

        if (operation.ResponseIsArray && operation.ResponseArrayItemsRef != null) {
            return new GenericTypeDefinition(
                typeof(List<>),
                new[] {
                    TypeDefinition.Get(
                        modelsNamespace,
                        NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseArrayItemsRef)))
                });
        }

        return null;
    }

    /// <summary>
    /// The status's name, on the same scheme <c>ErrorResponseEmitter</c> uses.
    /// </summary>
    /// <remarks>
    /// Duplicated from that emitter rather than shared, because the two schemes must be free to
    /// diverge without one silently renaming the other's types - and a generated type name is API.
    /// </remarks>
    private static string StatusName(int statusCode) {
        switch (statusCode) {
            case 400: return "BadRequest";
            case 401: return "Unauthorized";
            case 403: return "Forbidden";
            case 404: return "NotFound";
            case 405: return "MethodNotAllowed";
            case 406: return "NotAcceptable";
            case 409: return "Conflict";
            case 410: return "Gone";
            case 412: return "PreconditionFailed";
            case 415: return "UnsupportedMediaType";
            case 422: return "UnprocessableContent";
            case 429: return "TooManyRequests";
            case 500: return "InternalServerError";
            case 503: return "ServiceUnavailable";
            default: return "Status" + statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    private readonly struct CaseType {

        public CaseType(ClassDefinition definition, string name, bool hasBody) {
            Definition = definition;
            Name = name;
            HasBody = hasBody;
        }

        public ClassDefinition Definition { get; }

        public string Name { get; }

        public bool HasBody { get; }
    }
}
