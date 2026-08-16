using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.Idl;

namespace Hardened.Idl.Emitters;

/// <summary>
/// The exception an implementation throws to produce a response the specification declares.
/// </summary>
/// <remarks>
/// <para>
/// One type per operation and status - <c>GetPetNotFoundException</c> - rather than one shared type
/// per status. It names the operation it belongs to, so what a handler is allowed to throw is
/// discoverable from the handler, and it avoids colliding with the framework's own
/// <c>BadRequestException</c>, which a status-only name would.
/// </para>
/// <para>
/// The signature is unchanged: <c>Task&lt;Pet&gt; GetPet(string petId)</c> still returns a pet, and
/// the declared 404 arrives by being thrown. That is what makes this non-breaking - expressing
/// error responses in the return type would rewrite every existing signature, for a case most
/// specifications do not have.
/// </para>
/// </remarks>
internal static class ErrorResponseEmitter {

    public static IReadOnlyList<ClassDefinition> Emit(
        IConstructContainer container, ServiceModel service, string modelsNamespace) {
        var emitted = new List<ClassDefinition>();

        foreach (var operation in service.Operations) {
            foreach (var response in operation.ErrorResponses) {
                emitted.Add(EmitException(container, operation, response, modelsNamespace));
            }
        }

        return emitted;
    }

    private static ClassDefinition EmitException(
        IConstructContainer container, OperationModel operation, ErrorResponseModel response,
        string modelsNamespace) {
        var name =
            operation.MethodName +
            StatusName(response.StatusCode) +
            "Exception";

        var definition = container.AddClass(name);

        definition.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        definition.AddBaseType(
            TypeDefinition.Get("Hardened.Requests.Abstract.Errors", "StatusCodeException"));

        definition.Comment = DocComment.Format(response.Description)
            ?? $"The {response.StatusCode} response declared for {operation.HttpMethod} {operation.Path}.";

        var constructor = definition.AddConstructor(
            new CodeOutputComponent(
                response.Ref == null
                    ? $"base({response.StatusCode})"
                    : $"base({response.StatusCode}, value)") { Indented = false });

        constructor.Modifiers |= ComponentModifier.Public;

        if (response.Ref != null) {
            var payload = TypeDefinition.Get(
                modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(response.Ref)));

            constructor.AddParameter(payload, "value");

            // Typed access to the body, which the base can only offer as object. Named Body
            // rather than hiding the base's Value with a new member: a reader seeing Value on the
            // derived type would have no way to tell it was not the one they knew about.
            var property = definition.AddProperty(payload, "Body");

            property.Modifiers |= ComponentModifier.Public;
            property.Set = null;
            property.Get.LambdaSyntax = true;

            // Raw code, so it bypasses the output context and would keep the short name while
            // every type around it is qualified - the one cast in this file that could still bind
            // to a consumer's type of the same name.
            var cast = new StringBuilder();
            payload.WriteTypeName(cast, TypeOutputMode.Global);

            property.Get.AddCode($"({cast})Value!;");
        }

        return definition;
    }

    /// <summary>
    /// The status as a name. Anything without a well-known one keeps its number, which reads badly
    /// but is unambiguous - and a specification using 418 deserves a type as much as one using 404.
    /// </summary>
    private static string StatusName(int statusCode) =>
        statusCode switch {
            400 => "BadRequest",
            401 => "Unauthorized",
            402 => "PaymentRequired",
            403 => "Forbidden",
            404 => "NotFound",
            405 => "MethodNotAllowed",
            406 => "NotAcceptable",
            408 => "RequestTimeout",
            409 => "Conflict",
            410 => "Gone",
            412 => "PreconditionFailed",
            413 => "PayloadTooLarge",
            415 => "UnsupportedMediaType",
            422 => "UnprocessableEntity",
            423 => "Locked",
            429 => "TooManyRequests",
            500 => "InternalServerError",
            501 => "NotImplemented",
            502 => "BadGateway",
            503 => "ServiceUnavailable",
            504 => "GatewayTimeout",
            _ => "Status" + statusCode
        };
}
