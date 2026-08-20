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

        // Ahead of ResponseRef for the same reason RawBytesResponse is: it overrides what a schema
        // alone would say. itemSchema means the body is many of these one after another, which is
        // IAsyncEnumerable<T> and not Task<T> - and a Task<T> here would generate a client that
        // reads one item and stops.
        if (operation.ItemSchemaRef != null) {
            // By name rather than typeof(IAsyncEnumerable<>). This assembly targets netstandard2.0
            // and declares no package references at all, which is the property that keeps the IDL
            // layer unable to reference an OpenAPI reader - IAsyncEnumerable<> would need
            // Microsoft.Bcl.AsyncInterfaces, and buying one type with the first package reference
            // here would be a poor trade.
            return new GenericTypeDefinition(
                TypeDefinitionEnum.InterfaceDefinition,
                "System.Collections.Generic",
                "IAsyncEnumerable",
                new[] { Model(operation.ItemSchemaRef, modelsNamespace) });
        }

        if (operation.ResponseRef != null) {
            // Nullable exactly when the operation declares a 404, so the signature states what the
            // handler is allowed to do. Returning null answers 404 with the body the document
            // declared for it; without a declared 404 the `?` is absent and the compiler says so.
            //
            // A handler that wants to explain the refusal throws the generated exception type
            // instead, which carries a body it wrote. Null is the "nothing to say" answer.
            return Task(Model(operation.ResponseRef, modelsNamespace, DeclaresNotFound(operation)));
        }

        if (operation.ResponseIsArray && operation.ResponseArrayItemsRef != null) {
            return Task(new GenericTypeDefinition(
                typeof(List<>), new[] { Model(operation.ResponseArrayItemsRef, modelsNamespace) }));
        }

        // An array of primitives - List<string> rather than JsonElement. Only the $ref branch above
        // existed, so `items: {type: string}` had nothing to name and fell through to the untyped
        // response at the bottom of this method.
        if (operation.ResponseIsArray && operation.ResponseArrayItemsType != null) {
            var itemType = TypeMapper.MapToCSharpType(
                operation.ResponseArrayItemsType, operation.ResponseArrayItemsFormat);

            if (itemType != "object") {
                return Task(new GenericTypeDefinition(
                    typeof(List<>),
                    new[] { TypeMapper.GetTypeDefinition(modelsNamespace, itemType, false) }));
            }
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

    private static ITypeDefinition Model(
        string reference, string modelsNamespace, bool nullable = false) =>
        TypeMapper.GetTypeDefinition(
            modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(reference)), nullable);

    /// <summary>
    /// Whether a null return is a declared answer for this operation.
    /// </summary>
    /// <remarks>
    /// Restricted to the verbs whose null result is a 404 - a null POST or DELETE succeeds with no
    /// content, so <c>?</c> there would say something different from what the runtime does. See
    /// <c>NullValueResponseHandler</c> and <c>DefaultErrorBody</c>, which is where that rule lives.
    /// </remarks>
    private static bool DeclaresNotFound(OperationModel operation) =>
        (operation.HttpMethod == "GET" || operation.HttpMethod == "PUT") &&
        operation.ErrorResponses.Any(error => error.StatusCode == 404);

    private static ITypeDefinition Task(ITypeDefinition result) =>
        new GenericTypeDefinition(
            TypeDefinitionEnum.ClassDefinition, "System.Threading.Tasks", "Task", new[] { result });
}
