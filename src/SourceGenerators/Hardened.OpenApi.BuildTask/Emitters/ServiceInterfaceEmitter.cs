using System.Collections.Generic;
using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.SourceGenerator.Emitters;

/// <summary>
/// One service tag, as the interface a handler implements.
/// </summary>
internal static class ServiceInterfaceEmitter {

    public static InterfaceDefinition Emit(
        IConstructContainer container, ServiceModel service, string modelsNamespace) {
        var interfaceDefinition = container.AddInterface(NamingHelper.ToInterfaceName(service.Tag));

        interfaceDefinition.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;

        foreach (var operation in service.Operations) {
            var method = interfaceDefinition.AddMethod(NamingHelper.ToMethodName(operation.OperationId));

            method.Comment =
                $"{operation.HttpMethod} {operation.Path} &rarr; {operation.SuccessStatusCode}";

            method.SetReturnType(GetReturnType(operation, modelsNamespace));

            AddParameters(method, operation, modelsNamespace);
        }

        return interfaceDefinition;
    }

    internal static ITypeDefinition GetReturnType(OperationModel operation, string modelsNamespace) {
        if (operation.ResponseRef != null) {
            return Task(Model(operation.ResponseRef, modelsNamespace));
        }

        if (operation.ResponseIsArray && operation.ResponseArrayItemsRef != null) {
            return Task(new GenericTypeDefinition(
                typeof(List<>), new[] { Model(operation.ResponseArrayItemsRef, modelsNamespace) }));
        }

        if (operation.ResponseType != null) {
            var csType = TypeMapper.MapToCSharpType(operation.ResponseType, operation.ResponseFormat);

            // "object" means the spec declared a body with no usable shape, which is a Task with no
            // result rather than a Task<object> nobody can do anything with.
            if (csType != "object") {
                return Task(TypeMapper.GetTypeDefinition(modelsNamespace, csType, false));
            }
        }

        return TypeDefinition.Get("System.Threading.Tasks", "Task");
    }

    private static void AddParameters(
        MethodDefinition method, OperationModel operation, string modelsNamespace) {
        foreach (var parameter in operation.Parameters) {
            // Header parameters are not surfaced on the interface - see finding 3.6.
            if (parameter.In != "path" && parameter.In != "query") {
                continue;
            }

            var csType = TypeMapper.MapParameterToCSharpType(parameter);

            method.AddParameter(
                TypeMapper.GetTypeDefinition(modelsNamespace, csType, !parameter.IsRequired),
                NamingHelper.ToParameterName(parameter.Name));
        }

        if (operation.RequestBodyRef != null) {
            method.AddParameter(Model(operation.RequestBodyRef, modelsNamespace), "body");
        } else if (operation.RequestBodyType != null) {
            var csType = TypeMapper.MapToCSharpType(operation.RequestBodyType, null);
            method.AddParameter(TypeMapper.GetTypeDefinition(modelsNamespace, csType, false), "body");
        }
    }

    private static ITypeDefinition Model(string reference, string modelsNamespace) =>
        TypeDefinition.Get(
            modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(reference)));

    private static ITypeDefinition Task(ITypeDefinition result) =>
        new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, "System.Threading.Tasks", "Task", new[] { result });
}
