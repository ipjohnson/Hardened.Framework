using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.Idl;

namespace Hardened.Idl.SourceGenerator;

internal static class RequestModelBuilder {
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
                    spec.ValidatedOperations, filterTypeLookup, service.DispatchHeader);
                models.Add(model);
            }
        }

        return models;
    }

    public static List<RequestHandlerModel> EnrichWithHandlerFilters(
        List<RequestHandlerModel> models,
        IReadOnlyList<HandlerInfo> handlerInfos) {
        if (handlerInfos.Count == 0) return models;

        var result = new List<RequestHandlerModel>(models.Count);

        foreach (var model in models) {
            var handlerInfo = FindHandlerInfo(model, handlerInfos);
            if (handlerInfo != null) {
                var filters = new List<AttributeModel>(model.Filters);
                filters.AddRange(handlerInfo.ClassFilters);

                // Find method-level filters matching this handler's method
                var responseInformation = model.ResponseInformation;

                foreach (var methodFilter in handlerInfo.MethodFilters) {
                    if (string.Equals(methodFilter.MethodName, model.HandlerMethod,
                            StringComparison.Ordinal)) {
                        filters.AddRange(methodFilter.Filters);

                        // Which view renders a response is how the operation is fulfilled, not part
                        // of the contract it publishes - so it is read from the implementation and
                        // there is nothing in the document to override or be overridden by.
                        if (methodFilter.OutputType != null) {
                            responseInformation =
                                responseInformation with { OutputType = methodFilter.OutputType };
                        }

                        break;
                    }
                }

                result.Add(new RequestHandlerModel(
                    model.Name,
                    model.ControllerType,
                    model.HandlerMethod,
                    model.InvokeHandlerType,
                    model.RequestParameterInformationList,
                    responseInformation,
                    filters) {
                    // Carried across: this rebuilds the model to add [Handler] filters, and dropping
                    // it here leaves Parameters not implementing the interface its validator is
                    // typed on - so the filter's type test fails and validation silently does not
                    // run, on a build that is otherwise green.
                    ParametersInterface = model.ParametersInterface,
                });
            } else {
                result.Add(model);
            }
        }

        return result;
    }


    private static HandlerInfo? FindHandlerInfo(
        RequestHandlerModel model,
        IReadOnlyList<HandlerInfo> handlerInfos) {
        foreach (var info in handlerInfos) {
            if (info.InterfaceType.Name == model.ControllerType.Name) {
                return info;
            }
        }

        return null;
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
        IReadOnlyList<ValidatedOperationModel> validatedOperations,
        Dictionary<string, FilterTypeModel> filterTypeLookup,
        string? dispatchHeader = null) {
        var methodName = operation.MethodName;
        var handlerTypeName = $"{handlerClassPrefix}_{methodName}";
        var invokeHandlerType = TypeDefinition.Get(generatedNamespace, handlerTypeName);

        // The header comes from the service and the token from the operation, because that is where
        // each is declared - a protocol names the header once and every operation carries its own
        // target. Both null is ordinary path routing.
        var nameModel = new RequestHandlerNameModel(
            operation.Path, operation.HttpMethod, dispatchHeader, operation.DispatchKey);

        var parameters = BuildParameters(operation, modelsNamespace);
        var responseInfo = BuildResponseInfo(operation, modelsNamespace);

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
            serviceType,
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
            var csType = TypeMapper.MapParameterToCSharpType(param);
            var typeDefinition = TypeMapper.GetTypeDefinition(modelsNamespace, csType, param.IsCSharpNullable);

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

        if (operation.RequestBodyRef != null) {
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
        OperationModel operation, string modelsNamespace) {
        ITypeDefinition? returnType = null;

        if (operation.ResponseRef != null) {
            var typeName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseRef));
            returnType = TypeDefinition.Get(modelsNamespace, typeName);
        } else if (operation.ResponseIsArray && operation.ResponseArrayItemsRef != null) {
            var itemTypeName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(operation.ResponseArrayItemsRef));
            var itemType = TypeDefinition.Get(modelsNamespace, itemTypeName);
            returnType = new GenericTypeDefinition(typeof(List<>), new[] { itemType });
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
            RendersAModel = operation.ResponseRef != null || operation.ResponseIsArray
        };
    }


}
