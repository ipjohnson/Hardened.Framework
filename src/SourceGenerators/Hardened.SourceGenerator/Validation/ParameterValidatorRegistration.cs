using System.Collections.Generic;
using System.Threading;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Validation;

/// <summary>
/// Registers each handler's generated <c>Parameters</c> validator with the container.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the web and function generators, which each write their own dependency-injection
/// method and would otherwise derive this separately. The validators themselves are emitted by
/// <see cref="HandlerValidationGenerator"/> one handler at a time, and registration has to be
/// written once per entry point - so the name travels on the handler model, and whichever generator
/// owns the entry point writes the registration.
/// </para>
/// <para>
/// Registering rather than constructing is what lets the container pick the validator's
/// dependency-injection constructor, which takes
/// <c>IEnumerable&lt;IValidatorFor&lt;Nested&gt;&gt;</c> for each nested type. A validator built
/// directly would use the standalone constructor instead and see only the generated validator for
/// each nested type, silently ignoring one a consumer had registered.
/// </para>
/// </remarks>
internal static class ParameterValidatorRegistration {

    public static void Write(
        MethodDefinition diMethod, InstanceDefinition serviceCollection,
        IReadOnlyList<RequestHandlerModel> handlers, CancellationToken cancellationToken) {
        foreach (var model in handlers) {
            cancellationToken.ThrowIfCancellationRequested();

            if (model.ParametersValidator is not { } validator) {
                continue;
            }

            // Parameters is nested inside the generated invoke class, so it is only nameable through
            // it - the validator's own file spells the type the same way.
            var parameters = TypeDefinition.Get(
                model.InvokeHandlerType.Namespace, model.InvokeHandlerType.Name + ".Parameters");

            var validatorFor = new GenericTypeDefinition(
                TypeDefinitionEnum.InterfaceDefinition,
                "ValidationModules",
                "IValidatorFor",
                new[] { parameters });

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric(
                "AddSingleton", new ITypeDefinition[] { validatorFor, validator }));
        }
    }
}
