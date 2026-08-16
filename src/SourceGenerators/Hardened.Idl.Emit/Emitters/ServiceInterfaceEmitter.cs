using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.Idl.Emitters;

/// <summary>
/// One service tag, as the interface a handler implements.
/// </summary>
internal static class ServiceInterfaceEmitter {

    public static InterfaceDefinition Emit(
        IConstructContainer container, ServiceModel service, string modelsNamespace) {
        var interfaceDefinition = container.AddInterface(NamingHelper.ToInterfaceName(service.TypeBaseName));

        interfaceDefinition.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;

        foreach (var operation in service.Operations) {
            var method = interfaceDefinition.AddMethod(operation.MethodName);

            // The route line first, so it stays where a reader and the existing tests expect it,
            // and the spec's own prose below it after a blank line. A spec that says nothing reads
            // as it always did.
            var description = DocComment.Format(operation.Description);

            method.Comment =
                $"{operation.HttpMethod} {operation.Path} &rarr; {operation.SuccessStatusCode}" +
                (description == null ? "" : "\n\n" + description);

            if (operation.IsDeprecated) {
                Deprecation.Apply(method);
            }

            method.SetReturnType(GetReturnType(operation, modelsNamespace));

            AddParameters(method, operation, modelsNamespace);
        }

        return interfaceDefinition;
    }

    internal static ITypeDefinition GetReturnType(OperationModel operation, string modelsNamespace) {
        // Ahead of the schema, because it is a deliberate override of it. x-hardened-raw-bytes says
        // the application holds this payload already encoded, which the schema has no way to say -
        // type: string describes the wire, not what the handler is holding.
        if (operation.RawBytesResponse) {
            return Task(TypeDefinition.Get(typeof(byte[])));
        }

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
            var csType = TypeMapper.MapParameterToCSharpType(parameter);

            var emitted = method.AddParameter(
                TypeMapper.GetTypeDefinition(modelsNamespace, csType, parameter.IsCSharpNullable),
                parameter.MemberName);

            emitted.Comment = DocComment.Format(parameter.Description);
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
