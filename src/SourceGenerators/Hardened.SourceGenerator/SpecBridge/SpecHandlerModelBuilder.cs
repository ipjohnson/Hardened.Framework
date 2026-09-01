using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.Generation;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Turns an <see cref="OperationModel"/> into the <see cref="RequestHandlerModel"/> the emitters
/// consume.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the spine both generator wrappers compile in, rather than beside the description
/// reader, because it is no longer only the described front-ends that need it: code-first is moving
/// onto the same model, and a bridge compiled into one generator is a bridge the other cannot cross.
/// </para>
/// <para>
/// Everything that reads a type from a string is here; everything that reads one from a compilation
/// hands it over through <see cref="OperationSymbols"/> and this prefers it. That is the whole of
/// what makes one model serve both.
/// </para>
/// </remarks>
internal static class SpecHandlerModelBuilder {
    public static List<RequestHandlerModel> BuildModels(
        ServiceSpecModel spec,
        string modelsNamespace,
        string servicesNamespace,
        string generatedNamespace,
        string validationNamespace,
        IReadOnlyDictionary<string, OperationSymbols>? symbols = null) {
        var models = new List<RequestHandlerModel>();

        // Build lookup for x-filter-types by short name
        var filterTypeLookup = new Dictionary<string, FilterTypeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var ft in spec.FilterTypes) {
            filterTypeLookup[ft.Name] = ft;
        }

        foreach (var service in spec.Services) {
            var interfaceName = NamingHelper.ToInterfaceName(service.TypeBaseName);
            var serviceType = TypeDefinition.Get(servicesNamespace, interfaceName);
            var handlerClassPrefix = NamingHelper.ToControllerName(service.TypeBaseName);

            foreach (var operation in service.Operations) {
                var model = BuildHandlerModel(operation, serviceType, handlerClassPrefix,
                    modelsNamespace, generatedNamespace, validationNamespace,
                    spec.ResponseModel,
                    spec.ValidatedOperations, filterTypeLookup, spec.Schemas,
                    service.DispatchHeader, Symbols(symbols, operation));
                models.Add(model);
            }
        }

        return models;
    }




    /// <summary>
    /// Derives the controller name from an interface name.
    /// e.g. "IPetService" → strip "I" prefix and "Service" suffix → "Pet" → "PetController"
    /// </summary>
    internal static string DeriveControllerName(string interfaceName) {
        var name = interfaceName;

        // Strip leading "I" if followed by uppercase
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1])) {
            name = name.Substring(1);
        }

        // Strip trailing "Service"
        if (name.EndsWith("Service")) {
            name = name.Substring(0, name.Length - "Service".Length);
        }

        return name + "Controller";
    }

    private static RequestHandlerModel BuildHandlerModel(
        OperationModel operation,
        ITypeDefinition serviceType,
        string handlerClassPrefix,
        string modelsNamespace,
        string generatedNamespace,
        string validationNamespace,
        SpecResponseModel responseModel,
        IReadOnlyList<ValidatedOperationModel> validatedOperations,
        Dictionary<string, FilterTypeModel> filterTypeLookup,
        IReadOnlyList<SchemaModel> schemas,
        string? dispatchHeader = null,
        OperationSymbols? symbols = null) {
        var methodName = operation.MethodName;

        // Derived by convention from the service's name for a described application, because a
        // description names no C# types. An application that declared its own says so through
        // OperationSymbols, and its names are not guessable from anything in the model - the
        // handler class is named for a controller nobody wrote down here.
        var invokeHandlerType = symbols?.InvokeHandlerType
                                ?? TypeDefinition.Get(generatedNamespace, $"{handlerClassPrefix}_{methodName}");

        var declaringType = symbols?.ControllerType ?? serviceType;

        // The header comes from the service and the token from the operation, because that is where
        // each is declared - a protocol names the header once and every operation carries its own
        // target. Both null is ordinary path routing.
        var nameModel = new RequestHandlerNameModel(
            ConstrainedPath(operation), operation.HttpMethod, dispatchHeader, operation.DispatchKey);

        var parameters = BuildParameters(operation, modelsNamespace, symbols);
        var responseInfo = symbols?.ResponseInformation
                           ?? BuildResponseInfo(operation, schemas, modelsNamespace, responseModel);

        var filters = new List<AttributeModel>();

        // The validation the build task emitted for this operation, if any. The names come from the
        // model rather than being derived here: the task named them, and deriving them a second time
        // is how the two drift.
        var validated = validatedOperations.FirstOrDefault(v => v.OperationId == operation.OperationId);
        ITypeDefinition? parametersInterface = null;

        if (validated != null) {
            parametersInterface = TypeDefinition.Get(validationNamespace, validated.InterfaceName);

            // No validator argument: the attribute resolves every IValidatorFor<T> registered for
            // the interface, which is what lets a hand-written one run alongside the generated one.
            // Registration is emitted by Hardened.Validation.SourceGenerator into this application's
            // entry point, so nothing has to be wired by hand.
            filters.Add(new AttributeModel(
                new GenericTypeDefinition(
                    TypeDefinitionEnum.ClassDefinition,
                    "Hardened.Requests.Runtime.Validation",
                    "ValidateAttribute",
                    new[] { parametersInterface }),
                "",
                ""));
        }

        // What the description said this operation requires of its caller.
        //
        // Emitted as one more entry in the handler's metadata rather than handed to
        // ExecutionRequestHandlerInfo directly, because that reads
        // `requirement ?? RequirementFrom(Metadata)` - passing it there would silence an
        // [AuthorizeGrants] written on the implementation instead of composing with it.
        if (AuthorizationExpression(operation) is { } authorization) {
            filters.Add(new AttributeModel(
                TypeDefinition.Get(
                    "Hardened.Requests.Runtime.Authorization", "DescribedAuthorization"),
                authorization,
                ""));
        }

        // Wire in x-filters as typed attribute instances
        foreach (var filterInstance in operation.FilterInstances) {
            if (filterTypeLookup.TryGetValue(filterInstance.FilterTypeName, out var filterType)) {
                var attrType = TypeDefinition.Get(filterType.Namespace, filterType.ClassName);

                // Build property assignment string: "MaxRequests = 100, WindowSeconds = 60"
                var propAssignment = string.Join(", ",
                    filterInstance.PropertyValues.Select(kvp =>
                        $"{kvp.Key} = {FormatPropertyValue(kvp.Key, kvp.Value, filterType)}"));

                filters.Add(new AttributeModel(attrType, "", propAssignment));
            }
        }

        return new RequestHandlerModel(
            nameModel,
            declaringType,
            methodName,
            invokeHandlerType,
            parameters,
            responseInfo,
            filters) {
            ParametersInterface = parametersInterface,

            // The payload shapes, from the model. JsonSchemaWriter cannot produce these here - it
            // walks a type symbol, and these types are written by the build task rather than
            // declared in the consumer's source - so the published document carried paths and
            // operation ids and no schemas at all.
            RequestSchema = SpecSchemaWriter.ForRef(operation.RequestBodyRef, schemas),
            ResponseSchemas = BuildResponseSchemas(operation, schemas),

            // What the operation says about itself. Carried here rather than left to each caller,
            // because a handler model that has lost its summary cannot be told from one whose
            // operation never had a summary - and the document written from it is silently poorer.
            Tag = operation.Tag,
            Summary = operation.Summary,
            Description = operation.Description,
            IsDeprecated = operation.IsDeprecated,
            SecurityRequirements = operation.SecurityRequirements,
            // Parameter interfaces and body constraints both end at the same generated 400. Only
            // for a described operation: everything a description constrains reaches a generated
            // validator, while a code-first operation's bridge model carries required-ness and no
            // body facts, so the same test would promise a 400 nothing generates. Symbols are how
            // code-first announces itself.
            HasGeneratedValidation = validated != null ||
                                     (symbols == null && operation.HasValidationConstraints),
        };
    }

    /// <summary>
    /// Formats a property value as a C# literal based on the property's type
    /// from the filter type definition.
    /// </summary>
    private static string FormatPropertyValue(string propertyName, string value, FilterTypeModel filterType) {
        var prop = filterType.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (prop?.EnumType != null) {
            return $"{prop.EnumType}.{value}";
        }

        var csType = prop?.CSharpType ?? "string";

        return csType switch {
            "int" or "long" or "float" or "double" => value,
            "bool" => value.ToLowerInvariant(),
            _ => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
        };
    }

    private static IReadOnlyList<RequestParameterInformation> BuildParameters(
        OperationModel operation, string modelsNamespace, OperationSymbols? symbols = null) {
        var parameters = new List<RequestParameterInformation>();
        var index = 0;

        foreach (var param in operation.Parameters) {
            // Still needed either way: the default literal is formatted against the C# spelling.
            var csType = TypeMapper.MapParameterToCSharpType(param);

            // A model built from a compilation already holds the type; only a described one has to
            // spell it and map it back. See OperationSymbols.
            var typeDefinition = symbols?.Parameter(param.Name)
                                 ?? TypeMapper.GetTypeDefinition(modelsNamespace, csType, param.IsCSharpNullable);

            // Every location the specification allows is bound. The interface emitter and the
            // validation parameters interface take the same set - widen one without the others and
            // the generated Parameters class stops implementing its own interface.
            var bindType = symbols?.ParameterBindings != null &&
                           symbols.ParameterBindings.TryGetValue(param.Name, out var recorded)
                ? recorded
                : param.In switch {
                "path" => ParameterBindType.Path,
                "query" => ParameterBindType.QueryString,
                "header" => ParameterBindType.Header,
                "cookie" => ParameterBindType.Cookie,
                _ => ParameterBindType.QueryString
            };

            parameters.Add(new RequestParameterInformation(
                typeDefinition,
                param.MemberName,
                param.IsRequired,
                // Drives ParseWithDefault in the binder, so an absent value arrives as the
                // specification's default rather than as null.
                Default(symbols, param.Name) ?? DefaultLiteral.Format(param.Default, csType),
                bindType,
                param.Name,
                index++,
                Attribute(symbols, param.Name)) {
                // The prose the contract gives this parameter, for the published document. The
                // binder does not read it.
                Description = param.Description,
                // The declaration itself rides along for the same reader. The eight constructor
                // arguments above are what routing and binding need; the wire type, the constraint
                // bounds and the enum vocabulary are facts only the document wants, and carrying
                // the model beats re-deriving them from the C# type, which can only guess.
                SpecParameter = param,
            });
        }

        if (symbols?.RequestBodyType is { } knownBodyType) {
            parameters.Add(new RequestParameterInformation(
                knownBodyType,
                symbols?.RequestBodyName ?? "body",
                true,
                null,
                ParameterBindType.Body,
                "",
                index++));
        } else if (operation.RequestBodyRef != null) {
            var bodyTypeName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.RequestBodyRef));
            var bodyType = TypeDefinition.Get(modelsNamespace, bodyTypeName);

            parameters.Add(new RequestParameterInformation(
                bodyType,
                "body",
                true,
                null,
                ParameterBindType.Body,
                "",
                index++));
        } else if (operation.RequestBodyType != null) {
            var csType = TypeMapper.MapToCSharpType(operation.RequestBodyType, null);
            var bodyType = TypeMapper.GetTypeDefinition(modelsNamespace, csType, false);

            parameters.Add(new RequestParameterInformation(
                bodyType,
                "body",
                true,
                null,
                ParameterBindType.Body,
                "",
                index++));
        }

        return Ordered(parameters, symbols);
    }

    private static ResponseInformationModel BuildResponseInfo(
        OperationModel operation, IReadOnlyList<SchemaModel> schemas, string modelsNamespace,
        SpecResponseModel responseModel) {
        ITypeDefinition? returnType = null;

        // The declared set, ahead of everything below, exactly as ServiceInterfaceEmitter.GetReturnType
        // decides it - because that emitter writes the signature this dispatch has to fill.
        var unionCases = BuildUnionCases(operation, responseModel, modelsNamespace);

        if (unionCases != null) {
            return new ResponseInformationModel {
                IsAsync = true,
                ReturnType = new GenericTypeDefinition(
                    typeof(Task<>),
                    new[] { TypeDefinition.Get(modelsNamespace, ResponseSetPlan.ContainerName(operation)) }),
                DeclaredContentType = operation.ResponseContentType,
                RendersAModel = true,
                ProducedContentTypes = operation.ProducedContentTypes.Count > 0
                    ? string.Join(",", operation.ProducedContentTypes)
                    : null,
                UnionCases = unionCases
            };
        }

        if (operation.ResponseRef != null) {
            var typeName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseRef));
            returnType = TypeDefinition.Get(modelsNamespace, typeName);
        } else if (operation.ResponseIsArray && operation.ResponseArrayItemsRef != null) {
            var itemTypeName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseArrayItemsRef));
            var itemType = TypeDefinition.Get(modelsNamespace, itemTypeName);
            returnType = new GenericTypeDefinition(typeof(List<>), new[] { itemType });
        } else if (operation.ResponseIsArray && operation.ResponseArrayItemsType != null &&
                   TypeMapper.MapToCSharpType(
                       operation.ResponseArrayItemsType, operation.ResponseArrayItemsFormat) is var primitive &&
                   primitive != "object") {
            // Kept in step with ServiceInterfaceEmitter.GetReturnType: the interface declares the
            // signature and this types the handler that implements it, so a divergence here is a
            // generated class that does not implement its own interface.
            returnType = new GenericTypeDefinition(
                typeof(List<>),
                new[] { TypeMapper.GetTypeDefinition(modelsNamespace, primitive, false) });
        } else if (operation.ResponseType != null) {
            var csType = TypeMapper.MapToCSharpType(operation.ResponseType, operation.ResponseFormat);
            if (csType != "object") {
                returnType = TypeMapper.GetTypeDefinition(modelsNamespace, csType, false);
            }
        }

        // byte[] in place of the string the schema asked for, when the operation opted in. Written
        // before the Task<> wrap so the signature comes out Task<byte[]>.
        if (operation.RawBytesResponse) {
            returnType = TypeDefinition.Get(typeof(byte[]));
        }

        if (returnType != null) {
            returnType = new GenericTypeDefinition(typeof(Task<>), new[] { returnType });
        }

        return new ResponseInformationModel {
            IsAsync = true,
            ReturnType = returnType,
            DeclaredContentType = operation.ResponseContentType,

            // The payload carries its own headers where the contract binds them to its members,
            // which is Smithy's @httpHeader on an output. There is no response set on this path, so
            // the dispatch has to apply them beside the assignment or they never reach the wire.
            ReturnTypeProvidesHeaders = ResponseSetPlan.PrimarySuccessCarriesHeaders(operation),

            // Carried so the implementation can be checked against the contract: a document
            // promising rendered HTML for a model needs a view, and there is nothing to serialize
            // an object as text/html without one.
            RendersAModel = operation.ResponseRef != null || operation.ResponseIsArray,

            // The status the description declared for the success response.
            //
            // 200 is not carried, because it is what the response already answers - a handler info
            // field naming it would be noise on every operation to say nothing. Anything else is
            // the document making a promise the service has to keep.
            DefaultStatusCode =
                operation.SuccessStatusCode == 200 ? null : operation.SuccessStatusCode,

            NullResponseBodyExpression = NullResponseBody(operation, schemas, modelsNamespace),

            // The set the response is negotiated against. Empty means the description said nothing,
            // which leaves negotiation exactly as it was rather than declaring an empty set.
            ProducedContentTypes = operation.ProducedContentTypes.Count > 0
                ? string.Join(",", operation.ProducedContentTypes)
                : null
        };
    }

    /// <summary>
    /// The generated instance a null return writes, named, or null where there is none.
    /// </summary>
    /// <remarks>
    /// Both the condition and the field name come from <c>DefaultErrorBody</c> rather than being
    /// restated here. The build task that writes the field runs in a different process and this
    /// never sees its output, so naming a field it declined to emit would produce code that does not
    /// compile - the shared decision is what stops the two drifting.
    /// </remarks>
    private static string? NullResponseBody(
        OperationModel operation, IReadOnlyList<SchemaModel> schemas, string modelsNamespace) {
        var schemaName = DefaultErrorBody.SchemaFor(operation);

        if (schemaName == null) {
            return null;
        }

        // Emitted only when every required member can be filled without inventing a value.
        if (DefaultErrorBody.Arguments(
                schemas, schemaName, DefaultErrorBody.NullResponseStatus) == null) {
            return null;
        }

        return $"global::{modelsNamespace}.{DefaultErrorBody.HolderTypeName}." +
               DefaultErrorBody.FieldName(schemaName, DefaultErrorBody.NullResponseStatus);
    }



    /// <summary>
    /// The described authorization as a <c>Requirement</c> expression, or null where the description
    /// declared none.
    /// </summary>
    /// <remarks>
    /// An OR of ANDs, which is the shape both sides already have: <c>security</c> is an array of
    /// alternatives whose entries are conjoined, and <c>Requirement</c> is a boolean algebra over
    /// grants. Written out rather than reduced - a single branch with a single grant emits
    /// <c>Grant("x")</c> and not <c>AnyOf(AllOf(Grant("x")))</c> - because this lands in a generated
    /// file somebody will read.
    /// </remarks>
    private static string? AuthorizationExpression(OperationModel operation) {
        if (operation.AuthorizationBranches.Count == 0) {
            return null;
        }

        var branches = new List<string>();

        foreach (var branch in operation.AuthorizationBranches) {
            var terms = new List<string>();

            foreach (var grant in branch.Grants) {
                terms.Add(RequirementType + ".Grant(\"" + grant.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")");
            }

            if (branch.RequiresAuthentication) {
                terms.Add(RequirementType + ".Authenticated()");
            }

            if (terms.Count == 0) {
                continue;
            }

            branches.Add(terms.Count == 1
                ? terms[0]
                : RequirementType + ".AllOf(" + string.Join(", ", terms) + ")");
        }

        if (branches.Count == 0) {
            return null;
        }

        return branches.Count == 1
            ? branches[0]
            : RequirementType + ".AnyOf(" + string.Join(", ", branches) + ")";
    }

    private const string RequirementType =
        "global::Hardened.Requests.Abstract.Authorization.Requirement";

    /// <summary>
    /// The operation's declared response set, encoded for the shared dispatch emitter, or null
    /// where it answers with one type and throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This was missing, and its absence is the whole of why a specification-first response set did
    /// not work. InvokeMethodCodeGenerator emits a per-case switch when UnionCases is set and a
    /// plain assignment when it is not, and both directions share it - so the code-first path, which
    /// fills this from the return type's symbols, dispatched correctly while this one assigned the
    /// container itself. The wrapper went on the wire nested under its own Value member, at whatever
    /// status the operation would have answered anyway, and every declared error came back as a
    /// success carrying a Body property.
    /// </para>
    /// <para>
    /// Built from the contract rather than from symbols, because there are none here: this runs in a
    /// generator that is handed a parsed model and four namespaces, and reaching for a Compilation to
    /// resolve the generated container would defeat incrementality in the way EnabledFeatureSelector
    /// warns about. The contract already knows every case and its status.
    /// </para>
    /// <para>
    /// The branch order matches EmitContainer's, and every name comes from ResponseSetPlan, so the
    /// type the build task emits and the case this switches on cannot be spelled differently.
    /// </para>
    /// </remarks>
    private static string? BuildUnionCases(
        OperationModel operation, SpecResponseModel responseModel, string modelsNamespace) {
        if (!ResponseSetPlan.RequiresResponseSet(operation, responseModel)) {
            return null;
        }

        var cases = new List<UnionCaseModel>();

        // The payload type itself, and only while it stays bare. A primary success whose headers
        // are declared beside the body rather than on it is emitted as a wrapper instead, and the
        // loop below picks it up - adding it here as well would put two branches in the union for
        // one status.
        if (ResponseSetPlan.PrimarySuccessIsBarePayload(operation)) {
            cases.Add(new UnionCaseModel(
                Qualified(modelsNamespace, PrimarySuccessTypeName(operation, modelsNamespace)),
                operation.SuccessStatusCode,
                // A bare payload applies headers when the headers are its own members, which is what
                // Smithy's @httpHeader on an output produces.
                appliesHeaders: ResponseSetPlan.PrimarySuccessCarriesHeaders(operation),
                hasBody: true));
        }

        foreach (var success in operation.SuccessResponses) {
            if (!ResponseSetPlan.NeedsSuccessCaseType(operation, success)) {
                continue;
            }

            // A bodyless success is a case that serializes nothing - the 204 the union had no way to
            // express before. hasBody false is what makes the emitted switch set ShouldSerialize.
            //
            // Decided by the same three questions UnionResponseEmitter.PayloadType asks of the case
            // record it emits: a named schema, a list, or the scalar the contract typed without
            // naming. Keyed on Ref alone, a text/plain success - type: string, no $ref - built the
            // bodyless case a 204 gets, and the switch suppressed the one body the operation
            // existed to answer with.
            var bodyTypeName = SuccessBodyTypeName(success, modelsNamespace);
            var hasBody = bodyTypeName != null;

            cases.Add(new UnionCaseModel(
                Qualified(modelsNamespace, ResponseSetPlan.CaseName(operation, success.StatusCode)),
                success.StatusCode,
                appliesHeaders: success.Headers.Count > 0,
                hasBody: hasBody,
                carriesBody: hasBody,
                bodyTypeName: bodyTypeName));
        }

        foreach (var error in operation.ErrorResponses) {
            // carriesBody, because a generated error case is a wrapper whose Body is the payload the
            // document declared. Sending the wrapper ships that payload nested under a Body member.
            cases.Add(new UnionCaseModel(
                Qualified(modelsNamespace, ResponseSetPlan.CaseName(operation, error.StatusCode)),
                error.StatusCode,
                appliesHeaders: error.Headers.Count > 0,
                hasBody: true,
                carriesBody: true,
                bodyTypeName: error.Ref == null
                    ? null
                    : Qualified(modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(error.Ref)))));
        }

        return UnionResponseSelector.Encode(cases);
    }

    /// <summary>
    /// The type a wrapped success's Body member carries, or null for a bodyless case.
    /// </summary>
    /// <remarks>
    /// Must answer as <c>UnionResponseEmitter.PayloadType</c> does, because that emitter writes the
    /// record whose Body the emitted switch reads - the two halves run in different processes and
    /// meet only in the generated code. A shape neither can type is a bodyless case in both, which
    /// is at least visible at the handler.
    /// </remarks>
    private static string? SuccessBodyTypeName(
        SuccessResponseModel success, string modelsNamespace) {
        if (success.Ref != null) {
            return Qualified(
                modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(success.Ref)));
        }

        if (success.IsArray) {
            var item = success.ArrayItemsRef != null
                ? Qualified(
                    modelsNamespace,
                    NamingHelper.ToPascalCase(TypeMapper.GetRefName(success.ArrayItemsRef)))
                : ScalarBodyTypeName(success.ArrayItemsType, null);

            return item == null ? null : "global::System.Collections.Generic.List<" + item + ">";
        }

        return ScalarBodyTypeName(success.Type, success.Format);
    }

    private static string? ScalarBodyTypeName(string? type, string? format) {
        if (string.IsNullOrEmpty(type) || type == "object") {
            return null;
        }

        return TypeMapper.MapToCSharpType(type!, format);
    }

    /// <summary>The primary success's own type name, which the union names directly.</summary>
    /// <remarks>
    /// The list's item is qualified here, because <see cref="Qualified"/> sees the System. prefix
    /// and stops at the outer type - which emitted <c>case List&lt;Pet&gt;</c> with a bare
    /// <c>Pet</c> into a file with no using for it, CS0246 in generated code.
    /// </remarks>
    private static string PrimarySuccessTypeName(OperationModel operation, string modelsNamespace) =>
        operation.ResponseRef != null
            ? NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseRef))
            : "System.Collections.Generic.List<global::" + modelsNamespace + "." +
              NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseArrayItemsRef!)) + ">";

    /// <summary>
    /// global:: qualified, which is the form UnionCaseModel.TypeName is emitted as.
    /// </summary>
    /// <remarks>
    /// The code-first side gets this from SymbolDisplayFormat.FullyQualifiedFormat. There is no
    /// symbol here, so it is spelled - and it has to agree, because the emitted switch writes the
    /// string straight into a case label.
    /// </remarks>
    private static string Qualified(string modelsNamespace, string typeName) =>
        typeName.StartsWith("System.", System.StringComparison.Ordinal)
            ? "global::" + typeName
            : "global::" + modelsNamespace + "." + typeName;

    /// <summary>
    /// The operation's path with each constrained path parameter carrying its route constraint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The description says <c>/pets/{petId}</c> and declares the constraint separately; a route
    /// template says <c>/pets/{petId:slug}</c>. Composing them here is what puts a described
    /// constraint into the routing table rather than only into a validator - so a value that
    /// violates it means the route did not match, and the answer is 404 rather than a 400 about a
    /// URL naming no resource.
    /// </para>
    /// <para>
    /// Path parameters only. A query, header or body constraint judges a request that did name a
    /// resource and stays on the validation path, where 400 is the right answer.
    /// </para>
    /// </remarks>
    private static string ConstrainedPath(OperationModel operation) {
        var path = operation.Path;

        foreach (var parameter in operation.Parameters) {
            if (parameter.RouteConstraint == null) {
                continue;
            }

            path = path.Replace(
                "{" + parameter.Name + "}",
                "{" + parameter.Name + ":" + parameter.RouteConstraint + "}");
        }

        return path;
    }

    /// <summary>The symbols recorded for one operation, if any.</summary>
    private static OperationSymbols? Symbols(
        IReadOnlyDictionary<string, OperationSymbols>? symbols, OperationModel operation) =>
        symbols != null && symbols.TryGetValue(operation.OperationId, out var found) ? found : null;

    private static string? Default(OperationSymbols? symbols, string name) =>
        symbols?.ParameterDefaults != null &&
        symbols.ParameterDefaults.TryGetValue(name, out var value) ? value : null;

    private static AttributeModel? Attribute(OperationSymbols? symbols, string name) =>
        symbols?.ParameterAttributes != null &&
        symbols.ParameterAttributes.TryGetValue(name, out var attribute) ? attribute : null;

    /// <summary>
    /// Declaration order, when the builder recorded it.
    /// </summary>
    /// <remarks>
    /// A description lists its parameters and names its body separately, so recombining them can
    /// only ever put the body first or last. A method signature interleaves them, and the generated
    /// binder reads positionally.
    /// </remarks>
    private static IReadOnlyList<RequestParameterInformation> Ordered(
        List<RequestParameterInformation> parameters, OperationSymbols? symbols) {
        if (symbols?.ParameterOrder == null) {
            return parameters;
        }

        var order = symbols.ParameterOrder;

        return parameters
            .OrderBy(parameter => {
                var at = order.IndexOf(parameter.BindingName);

                return at < 0 ? order.IndexOf(parameter.Name) : at;
            })
            .Select((parameter, index) => new RequestParameterInformation(
                parameter.ParameterType, parameter.Name, parameter.Required, parameter.DefaultValue,
                parameter.BindingType, parameter.BindingName, index, parameter.CustomAttribute))
            .ToList();
    }
    /// <summary>
    /// Every status the operation declares, with the payload declared for it.
    /// </summary>
    /// <remarks>
    /// Successes and errors both, because a document that describes only the happy path leaves a
    /// generated client with no branch for the 404 the contract promised. The response's own
    /// description wins over the status's standard wording.
    /// </remarks>
    private static IReadOnlyList<ResponseSchemaModel> BuildResponseSchemas(
        OperationModel operation, IReadOnlyList<SchemaModel> schemas) {
        var result = new List<ResponseSchemaModel>();

        foreach (var success in operation.SuccessResponses) {
            result.Add(new ResponseSchemaModel(
                success.StatusCode,
                SpecSchemaWriter.DescriptionFor(success.Description, success.StatusCode),
                success.IsArray
                    ? SpecSchemaWriter.ForArrayOf(success.ArrayItemsRef, schemas)
                    // A success the contract types without naming - text/plain's string - still
                    // has a schema; publishing the status with no content told a client to read
                    // nothing from a response that carries the body.
                    : SpecSchemaWriter.ForRef(success.Ref, schemas)
                      ?? SpecSchemaWriter.ForScalar(success.Type, success.Format)) {
                Headers = success.Headers
            });
        }

        foreach (var error in operation.ErrorResponses) {
            result.Add(new ResponseSchemaModel(
                error.StatusCode,
                SpecSchemaWriter.DescriptionFor(error.Description, error.StatusCode),
                SpecSchemaWriter.ForRef(error.Ref, schemas)) {
                Headers = error.Headers
            });
        }

        return result;
    }

}
