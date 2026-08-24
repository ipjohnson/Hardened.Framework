using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.Idl;

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
        string validationNamespace) {
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
                    service.DispatchHeader);
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
        string? dispatchHeader = null) {
        var methodName = operation.MethodName;

        // Derived by convention from the service's name for a described application, because a
        // description names no C# types. An application that declared its own says so through
        // OperationSymbols, and its names are not guessable from anything in the model - the
        // handler class is named for a controller nobody wrote down here.
        var invokeHandlerType = operation.Symbols?.InvokeHandlerType
                                ?? TypeDefinition.Get(generatedNamespace, $"{handlerClassPrefix}_{methodName}");

        var declaringType = operation.Symbols?.ControllerType ?? serviceType;

        // The header comes from the service and the token from the operation, because that is where
        // each is declared - a protocol names the header once and every operation carries its own
        // target. Both null is ordinary path routing.
        var nameModel = new RequestHandlerNameModel(
            ConstrainedPath(operation), operation.HttpMethod, dispatchHeader, operation.DispatchKey);

        var parameters = BuildParameters(operation, modelsNamespace);
        var responseInfo = BuildResponseInfo(operation, schemas, modelsNamespace, responseModel);

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
        OperationModel operation, string modelsNamespace) {
        var parameters = new List<RequestParameterInformation>();
        var index = 0;

        foreach (var param in operation.Parameters) {
            // Still needed either way: the default literal is formatted against the C# spelling.
            var csType = TypeMapper.MapParameterToCSharpType(param);

            // A model built from a compilation already holds the type; only a described one has to
            // spell it and map it back. See OperationSymbols.
            var typeDefinition = operation.Symbols?.Parameter(param.Name)
                                 ?? TypeMapper.GetTypeDefinition(modelsNamespace, csType, param.IsCSharpNullable);

            // Every location the specification allows is bound. The interface emitter and the
            // validation parameters interface take the same set - widen one without the others and
            // the generated Parameters class stops implementing its own interface.
            var bindType = param.In switch {
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
                DefaultLiteral.Format(param.Default, csType),
                bindType,
                param.Name,
                index++));
        }

        if (operation.Symbols?.RequestBodyType is { } knownBodyType) {
            parameters.Add(new RequestParameterInformation(
                knownBodyType,
                "body",
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

        return parameters;
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

        if (ResponseSetPlan.HasNamedSuccessPayload(operation)) {
            cases.Add(new UnionCaseModel(
                Qualified(modelsNamespace, PrimarySuccessTypeName(operation)),
                operation.SuccessStatusCode,
                appliesHeaders: false,
                hasBody: true));
        }

        foreach (var success in operation.SuccessResponses) {
            if (!ResponseSetPlan.NeedsSuccessCaseType(operation, success)) {
                continue;
            }

            // A bodyless success is a case that serializes nothing - the 204 the union had no way to
            // express before. hasBody false is what makes the emitted switch set ShouldSerialize.
            var hasBody = success.Ref != null;

            cases.Add(new UnionCaseModel(
                Qualified(modelsNamespace, ResponseSetPlan.CaseName(operation, success.StatusCode)),
                success.StatusCode,
                appliesHeaders: false,
                hasBody: hasBody,
                carriesBody: hasBody,
                bodyTypeName: hasBody
                    ? Qualified(modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(success.Ref!)))
                    : null));
        }

        foreach (var error in operation.ErrorResponses) {
            // carriesBody, because a generated error case is a wrapper whose Body is the payload the
            // document declared. Sending the wrapper ships that payload nested under a Body member.
            cases.Add(new UnionCaseModel(
                Qualified(modelsNamespace, ResponseSetPlan.CaseName(operation, error.StatusCode)),
                error.StatusCode,
                appliesHeaders: false,
                hasBody: true,
                carriesBody: true,
                bodyTypeName: error.Ref == null
                    ? null
                    : Qualified(modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(error.Ref)))));
        }

        return UnionResponseSelector.Encode(cases);
    }

    /// <summary>The primary success's own type name, which the union names directly.</summary>
    private static string PrimarySuccessTypeName(OperationModel operation) =>
        operation.ResponseRef != null
            ? NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseRef))
            : "System.Collections.Generic.List<" +
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
}
