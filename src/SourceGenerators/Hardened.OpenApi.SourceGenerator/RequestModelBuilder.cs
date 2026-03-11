using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.OpenApi.SourceGenerator;

internal static class RequestModelBuilder {
    public static List<RequestHandlerModel> BuildModels(
        OpenApiSpecModel spec,
        string modelsNamespace,
        string servicesNamespace,
        string generatedNamespace) {
        var models = new List<RequestHandlerModel>();

        foreach (var service in spec.Services) {
            var interfaceName = NamingHelper.ToInterfaceName(service.Tag);
            var serviceType = TypeDefinition.Get(servicesNamespace, interfaceName);
            var handlerClassPrefix = NamingHelper.ToControllerName(service.Tag);

            foreach (var operation in service.Operations) {
                var model = BuildHandlerModel(operation, serviceType, handlerClassPrefix,
                    modelsNamespace, generatedNamespace);
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
                foreach (var methodFilter in handlerInfo.MethodFilters) {
                    if (string.Equals(methodFilter.MethodName, model.HandlerMethod,
                            StringComparison.Ordinal)) {
                        filters.AddRange(methodFilter.Filters);
                        break;
                    }
                }

                result.Add(new RequestHandlerModel(
                    model.Name,
                    model.ControllerType,
                    model.HandlerMethod,
                    model.InvokeHandlerType,
                    model.RequestParameterInformationList,
                    model.ResponseInformation,
                    filters));
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
        string generatedNamespace) {
        var methodName = NamingHelper.ToMethodName(operation.OperationId);
        var handlerTypeName = $"{handlerClassPrefix}_{methodName}";
        var invokeHandlerType = TypeDefinition.Get(generatedNamespace, handlerTypeName);

        var nameModel = new RequestHandlerNameModel(operation.Path, operation.HttpMethod);

        var parameters = BuildParameters(operation, modelsNamespace);
        var responseInfo = BuildResponseInfo(operation, modelsNamespace);

        var filters = new List<AttributeModel>();

        // Wire in validation filter provider if operation has constraints
        if (operation.HasValidationConstraints) {
            var filterProviderName = NamingHelper.ToPascalCase(operation.OperationId) + "_ValidationFilterProvider";
            var filterProviderType = TypeDefinition.Get(generatedNamespace, filterProviderName);
            filters.Add(new AttributeModel(filterProviderType, "", ""));
        }

        return new RequestHandlerModel(
            nameModel,
            serviceType,
            methodName,
            invokeHandlerType,
            parameters,
            responseInfo,
            filters);
    }

    private static IReadOnlyList<RequestParameterInformation> BuildParameters(
        OperationModel operation, string modelsNamespace) {
        var parameters = new List<RequestParameterInformation>();
        var index = 0;

        foreach (var param in operation.Parameters) {
            if (param.In != "path" && param.In != "query" && param.In != "header") continue;

            var csType = TypeMapper.MapParameterToCSharpType(param);
            var typeDefinition = TypeMapper.GetTypeDefinition(modelsNamespace, csType, !param.IsRequired);

            var bindType = param.In switch {
                "path" => ParameterBindType.Path,
                "query" => ParameterBindType.QueryString,
                "header" => ParameterBindType.Header,
                _ => ParameterBindType.QueryString
            };

            parameters.Add(new RequestParameterInformation(
                typeDefinition,
                NamingHelper.ToParameterName(param.Name),
                param.IsRequired,
                null,
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

        if (returnType != null) {
            returnType = new GenericTypeDefinition(typeof(Task<>), new[] { returnType });
        }

        return new ResponseInformationModel {
            IsAsync = true,
            ReturnType = returnType
        };
    }
}
