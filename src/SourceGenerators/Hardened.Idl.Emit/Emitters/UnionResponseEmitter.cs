using Hardened.Generation;
using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Generation.Models;

namespace Hardened.Idl.Emitters;

/// <summary>
/// An operation's declared responses, as a named union over the types they resolve to.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="ErrorResponseEmitter"/>, and it resolves a declared error to the same
/// place that one does. That one expresses a declared error by having the implementation throw,
/// leaving the signature saying <c>Task&lt;Pet&gt;</c>; this one puts the whole response set in the
/// return type. Which of the two runs is decided per operation, and only one of them runs for any
/// given operation - emitting both would offer two ways to answer the same 404 and no way to tell
/// which a handler used.
/// </para>
/// <para>
/// <b>The per-status wrapper is mandatory, not stylistic.</b> The repo's own <c>DeclaredErrors</c>
/// fixture declares 404 and 409 both <c>$ref</c>-ing <c>ApiError</c>, so the naive shape is
/// <c>Response&lt;Pet, ApiError, ApiError&gt;</c> - two identical conversions, which is CS0457 at
/// the point of use. Wrapping per status makes every case a distinct type.
/// </para>
/// <para>
/// <b>What is no longer mandatory is generating the wrapper.</b>
/// <c>Hardened.Requests.Abstract.Responses</c> ships one per status, so the same fixture is
/// <c>Response&lt;Pet, NotFound&lt;ApiError&gt;, Conflict&lt;ApiError&gt;&gt;</c> and nothing is
/// emitted for either case. The container is still generated per operation, which is what keeps the
/// unbounded arity <c>Response&lt;T1..T8&gt;</c> cannot give the specification-first path.
/// <see cref="ShippedResponses.For"/> decides; a case type is written here only for what it
/// declined - an error the description named, one carrying a header no shipped wrapper can hold, or
/// a status registered nowhere.
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

    /// <summary>Where the response contracts a generated case implements live.</summary>
    private const string ResponsesNamespace = ShippedResponses.Namespace;

    /// <summary>
    /// The union type and the success cases it needs, for every operation the service declares.
    /// </summary>
    /// <remarks>
    /// The error cases are not here: they are shared across operations and across services, so
    /// <see cref="EmitErrorCaseTypes"/> writes them once for the whole document. This still names
    /// them, because a branch is a name whether or not this emitter is the thing that wrote it.
    /// </remarks>
    public static IReadOnlyList<ClassDefinition> Emit(
        IConstructContainer container, ServiceModel service, string modelsNamespace,
        bool asLanguageUnion = false, SpecResponseModel responseModel = SpecResponseModel.Response) {
        var emitted = new List<ClassDefinition>();

        foreach (var operation in service.Operations) {
            if (!ResponseSetPlan.RequiresResponseSet(operation, responseModel)) {
                continue;
            }

            var branches = new List<ITypeDefinition>();

            var success = SuccessBranchType(operation, modelsNamespace);

            if (success != null) {
                branches.Add(success);
            }

            // A declared success that carries no body, or that is not the primary one, needs a case
            // type of its own - it has no schema to stand in for it the way a $ref success does.
            // 204 is the common shape: the branch exists, it is empty, and the dispatch already
            // knows what to do with a case whose hasBody is false.
            //
            // Still per operation, unlike the errors. A success case carries the operation's own
            // payload shape, so two operations declaring a 200 have nothing to share.
            foreach (var response in operation.SuccessResponses) {
                if (!ResponseSetPlan.NeedsSuccessCaseType(operation, response)) {
                    continue;
                }

                var name = ResponseSetPlan.CaseName(operation, response.StatusCode);

                var successCase = EmitCaseType(
                    container, name, response.StatusCode,
                    PayloadType(
                        response.Ref, response.Type, response.Format,
                        response.IsArray, response.ArrayItemsRef, response.ArrayItemsType,
                        modelsNamespace),
                    response.Description,
                    $"The {response.StatusCode} response declared for " +
                    $"{operation.HttpMethod} {operation.Path}.",
                    response.Headers);

                emitted.Add(successCase);
                branches.Add(TypeDefinition.Get(modelsNamespace, name));
            }

            foreach (var error in operation.ErrorResponses) {
                branches.Add(ErrorBranchType(error, modelsNamespace));
            }

            emitted.Add(EmitContainer(container, operation, branches, asLanguageUnion));
        }

        return emitted;
    }

    /// <summary>
    /// One case type per declared error the shipped set cannot express, for the whole document.
    /// </summary>
    /// <param name="errors">
    /// The distinct errors, one entry each. Computed by <see cref="SpecFileEmitter"/> rather than
    /// walked per operation here: two operations declaring one 404 over one schema want one case
    /// type, and emitting it per operation is what made <c>GetLabelNotFound</c> and
    /// <c>ArchiveLabelNotFound</c> two names for one record.
    /// </param>
    public static IReadOnlyList<ClassDefinition> EmitErrorCaseTypes(
        IConstructContainer container, IReadOnlyList<ErrorResponseModel> errors,
        string modelsNamespace) {
        var emitted = new List<ClassDefinition>();

        foreach (var error in errors) {
            emitted.Add(EmitCaseType(
                container, error.TypeName!, error.StatusCode,
                PayloadType(error.Ref, null, null, false, null, null, modelsNamespace),
                error.Description,
                // No operation in the fallback, and there cannot be one: this case is shared by
                // every operation that declares the error.
                $"The {error.StatusCode} response the description declares" +
                (error.Name == null ? "." : $" as '{error.Name}'."),
                error.Headers));
        }

        return emitted;
    }

    /// <summary>
    /// The type a declared error reaches the union as: a shipped response, or a generated case.
    /// </summary>
    /// <remarks>
    /// <see cref="ShippedResponses.For"/> is the one decision, asked here and by the Roslyn
    /// generator that writes the switch over these branches. The two run in different processes and
    /// meet only in the generated code, so a second derivation of it is a switch arm naming a type
    /// nothing emitted.
    /// </remarks>
    private static ITypeDefinition ErrorBranchType(
        ErrorResponseModel error, string modelsNamespace) {
        var binding = ShippedResponses.For(error);

        if (binding == null) {
            return TypeDefinition.Get(modelsNamespace, error.TypeName!);
        }

        var shipped = binding.Value;
        var payload = PayloadType(error.Ref, null, null, false, null, null, modelsNamespace);

        // Status<Http.Locked, Problem> - the escape hatch, for a registered status the framework
        // ships no record for. Two statuses are two closed types, so CS0457 never fires and the
        // framework does not have to know the number in advance.
        if (shipped.Marker != null) {
            var marker = TypeDefinition.Get(
                ShippedResponses.Namespace,
                ShippedResponses.MarkerHolderName + "." + shipped.Marker);

            return shipped.TakesBody && payload != null
                ? Shipped(shipped.TypeName, marker, payload)
                : Shipped(shipped.TypeName, marker);
        }

        return shipped.TakesBody && payload != null
            ? Shipped(shipped.TypeName, payload)
            : TypeDefinition.Get(ShippedResponses.Namespace, shipped.TypeName);
    }

    private static ITypeDefinition Shipped(string name, params ITypeDefinition[] arguments) =>
        new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, ShippedResponses.Namespace, name, arguments);

    /// <summary>
    /// One record per declared status, carrying the body that status declares, or nothing.
    /// </summary>
    /// <remarks>
    /// A record rather than a class, so two responses carrying equal bodies compare equal - which is
    /// what a test asserting on a handler's result wants, and what an exception could never give it.
    /// Sealed, because a case type assignable to another case type in the same set has no
    /// unambiguous match order - and partial alongside it, because those are different questions.
    /// Sealed forbids deriving; partial permits extending in place, which is how an application adds
    /// an interface or a computed member to a type it did not write. Sealing to keep the match order
    /// unambiguous never required refusing that.
    /// </remarks>
    private static ClassDefinition EmitCaseType(
        IConstructContainer container, string name, int statusCode, ITypeDefinition? payload,
        string? description, string fallbackComment,
        IReadOnlyList<ResponseHeaderModel>? headers = null) {
        var definition = container.AddClass(name);

        definition.TypeKeyword = ClassKeyword.Record;
        definition.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed | ComponentModifier.Partial;

        // A case type declares everything in its header, so it ends at the semicolon rather than
        // carrying an empty body. Legal either way; this is what anyone writing it by hand writes,
        // and generated code is read more often than it is written.
        definition.TerminateWithSemicolon = true;

        definition.Comment = DocComment.Format(description) ?? fallbackComment;

        // [HttpStatus], so the dispatch generator reads this case's status from the type rather
        // than from the specification it can no longer see. It is the same attribute a hand-written
        // response type carries, which is what keeps one status resolution serving both front ends.
        definition.AddAttribute(
            TypeDefinition.Get(ResponsesNamespace, "HttpStatusAttribute"),
            new CodeOutputComponent(
                statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)) {
                Indented = false
            });

        var headerParameters = ResolveHeaderParameters(headers, carriesBody: payload != null);

        if (payload == null) {
            // A 204 declaring a Location, or a 304 declaring an ETag - a case with nothing to
            // serialize and something to send. The constructor exists only where there are headers
            // to take, so a bodyless case that declares none is the parameterless record it was.
            if (headerParameters.Count > 0) {
                var headerOnlyConstructor = definition.AddConstructor();

                headerOnlyConstructor.IsPrimary = true;
                headerOnlyConstructor.Modifiers |= ComponentModifier.Public;

                AddHeaderParameters(headerOnlyConstructor, headerParameters);
                EmitApplyHeaders(definition, headerParameters);
            }

            return definition;
        }

        var constructor = definition.AddConstructor();

        // Primary, so Body is a property of the record rather than a constructor parameter that is
        // thrown away. An ordinary constructor with an empty body compiles and silently discards
        // the payload, which is the shape a handler would then find empty at run time.
        constructor.IsPrimary = true;
        constructor.Modifiers |= ComponentModifier.Public;
        constructor.AddParameter(payload, "Body");

        // After Body, so adding a header to a document does not renumber the positional arguments
        // every existing call site already passes.
        AddHeaderParameters(constructor, headerParameters);

        // The contract the shared dispatch reads a wrapper's payload through, and the same one
        // Created<T> and the generic problem types implement by hand. Without it the emitted switch
        // casts this case to ICarriesResponseBody and does not compile - and before the switch was
        // emitted at all, the wrapper went on the wire whole, putting the declared payload under a
        // Body member no client was told about.
        definition.AddBaseType(
            TypeDefinition.Get(ResponsesNamespace, "ICarriesResponseBody"));

        // A body to put it in. The header-only form above is what a case with no payload wants, and
        // it is what this was until the interface arrived - a record ending at its semicolon has
        // nowhere for a member to go, and CSharpAuthor drops one added to it silently.
        definition.TerminateWithSemicolon = false;

        // Explicit, because the record already has a public Body of the payload's own type and an
        // implicit implementation would have to widen it to object?. Created<T> resolves the same
        // collision the same way.
        definition.AddComponent(
            new CodeOutputComponent(
                "object? " + ResponsesNamespace + ".ICarriesResponseBody.Body => Body;") {
                Indented = true
            });

        EmitApplyHeaders(definition, headerParameters);

        return definition;
    }

    /// <summary>
    /// The parameter each declared header contributes, with collisions resolved.
    /// </summary>
    /// <remarks>
    /// A header's name is not a C# identifier - <c>X-Rate-Limit</c> has no spelling of its own, and
    /// <c>Content-Location</c> PascalCases onto nothing that clashes while <c>Body</c> would land
    /// exactly on the payload parameter. Suffixed rather than rejected, because a document is
    /// entitled to a header called Body and refusing to generate for it would be the generator
    /// imposing a C# rule on a wire format.
    /// </remarks>
    private static List<ResponseHeaderModel> ResolveHeaderParameters(
        IReadOnlyList<ResponseHeaderModel>? headers, bool carriesBody) {
        var resolved = new List<ResponseHeaderModel>();

        if (headers == null || headers.Count == 0) {
            return resolved;
        }

        var taken = new HashSet<string>(System.StringComparer.Ordinal);

        if (carriesBody) {
            taken.Add("Body");
        }

        foreach (var header in headers) {
            var candidate = string.IsNullOrEmpty(header.ParameterName)
                ? NamingHelper.ToPascalCase(header.Name)
                : header.ParameterName;

            if (string.IsNullOrEmpty(candidate)) {
                continue;
            }

            var unique = candidate;
            var suffix = 2;

            while (!taken.Add(unique)) {
                unique = candidate + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                suffix++;
            }

            resolved.Add(new ResponseHeaderModel {
                Name = header.Name,
                ParameterName = unique,
                Description = header.Description
            });
        }

        return resolved;
    }

    /// <summary>
    /// One string parameter per declared header.
    /// </summary>
    /// <remarks>
    /// String whatever the document types the header as. A header is a string on the wire, and an
    /// integer one still has to be formatted by whoever knows its units - an <c>ETag</c> is quoted,
    /// a <c>Retry-After</c> is either seconds or a date. Typing these would take that choice away
    /// from the only code that can make it.
    /// </remarks>
    private static void AddHeaderParameters(
        ConstructorDefinition constructor, IReadOnlyList<ResponseHeaderModel> headers) {
        foreach (var header in headers) {
            constructor.AddParameter(TypeDefinition.Get(typeof(string)), header.ParameterName);
        }
    }

    /// <summary>
    /// The interface the shared dispatch calls, implemented against the declared headers.
    /// </summary>
    /// <remarks>
    /// The same interface <c>Created&lt;T&gt;</c> implements by hand, so a generated case and a
    /// hand-written one reach the wire through one path. <c>UnionResponseSelector</c> already
    /// decides code-first's <c>AppliesHeaders</c> by asking whether the type implements this;
    /// emitting it here is what lets the specification-first builder ask the same question instead
    /// of answering it with a literal.
    /// </remarks>
    private static void EmitApplyHeaders(
        ClassDefinition definition, IReadOnlyList<ResponseHeaderModel> headers) {
        if (headers.Count == 0) {
            return;
        }

        // A record ending at its semicolon has nowhere for a method to go, and CSharpAuthor drops
        // one added to it silently.
        definition.TerminateWithSemicolon = false;

        definition.AddBaseType(
            TypeDefinition.Get(ResponsesNamespace, "IProvidesResponseHeaders"));

        var method = definition.AddMethod("ApplyHeaders");

        method.Modifiers |= ComponentModifier.Public;
        method.AddParameter(
            new GenericTypeDefinition(
                typeof(IDictionary<,>),
                new ITypeDefinition[] {
                    TypeDefinition.Get(typeof(string)),
                    TypeDefinition.Get("Microsoft.Extensions.Primitives", "StringValues")
                }),
            "headers");

        foreach (var header in headers) {
            method.AddIndentedStatement(
                "headers[\"" + header.Name + "\"] = " + header.ParameterName);
        }
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
        IConstructContainer container, OperationModel operation,
        IReadOnlyList<ITypeDefinition> branchTypes, bool asLanguageUnion) {
        var name = ResponseSetPlan.ContainerName(operation);

        var branches = new List<string>();

        foreach (var branchType in branchTypes) {
            branches.Add(QualifiedName(branchType));
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
    /// The body a declared response carries: its named schema, a list of one, or the scalar the
    /// contract typed without naming.
    /// </summary>
    /// <remarks>
    /// The scalar half is what a <c>text/plain</c> success is - <c>type: string</c>, no
    /// <c>$ref</c> - and it emitted a case record with nowhere to put the body, which compiled
    /// and could never carry the label it existed to answer with. A shape this still cannot type
    /// yields null and the bodyless case, which is at least visible at the handler.
    /// </remarks>
    private static ITypeDefinition? PayloadType(
        string? bodyRef, string? type, string? format,
        bool isArray, string? itemsRef, string? itemsType, string modelsNamespace) {
        if (bodyRef != null) {
            return TypeDefinition.Get(
                modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(bodyRef)));
        }

        if (isArray) {
            var item = itemsRef != null
                ? TypeDefinition.Get(
                    modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(itemsRef)))
                : ScalarType(itemsType, null);

            return item == null
                ? null
                : new GenericTypeDefinition(typeof(List<>), new[] { item });
        }

        return ScalarType(type, format);
    }

    private static ITypeDefinition? ScalarType(string? type, string? format) {
        if (string.IsNullOrEmpty(type) || type == "object") {
            return null;
        }

        var csType = TypeMapper.MapToCSharpType(type, format);

        // GetTypeDefinition rather than the primitive lookup alone, because byte[] and the
        // date types come back from the mapper too. An unmapped name lands in the models
        // namespace, which is a compile error at the case type rather than a silent empty record.
        return TypeMapper.GetTypeDefinition("", csType, false);
    }

    /// <summary>
    /// A branch as source text, type arguments included.
    /// </summary>
    /// <remarks>
    /// Namespace-plus-name dropped a generic's arguments, so an operation whose success is an
    /// array emitted constructors taking <c>System.Collections.Generic.List</c> - CS0305, in
    /// generated code, for a contract the parser accepted. The language-union path never had the
    /// bug because CSharpAuthor renders the type itself; this is the struct path catching up.
    /// </remarks>
    private static string QualifiedName(ITypeDefinition type) {
        if (type.TypeArguments.Count == 0) {
            return type.Namespace + "." + type.Name;
        }

        var arguments = new List<string>();

        foreach (var argument in type.TypeArguments) {
            arguments.Add(QualifiedName(argument));
        }

        return type.Namespace + "." + type.Name + "<" + string.Join(",", arguments) + ">";
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
        // Not HasNamedSuccessPayload: a primary success that declares headers is emitted as a
        // wrapper by the success loop and is already a branch, so naming the payload here too would
        // give the union two branches for one status.
        if (!ResponseSetPlan.PrimarySuccessIsBarePayload(operation)) {
            return null;
        }

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
}
