using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator;
using Hardened.OpenApi.SourceGenerator.Models;

namespace Hardened.OpenApi.BuildTask.Validation;

/// <summary>
/// The parameter interface one operation gets, when anything about it is constrained.
/// </summary>
/// <remarks>
/// <para>
/// The interface is the seam between this task and the source generator. The task cannot name the
/// handler's <c>Parameters</c> class - it is nested inside a handler type whose name carries a
/// computed suffix - but it can name an interface, put the constraints on it, and let the generator
/// make <c>Parameters</c> implement it.
/// </para>
/// <para>
/// No validator is emitted here. The constraints are attributes, and
/// <c>Hardened.Validation.SourceGenerator</c> reads them the same way it reads attributes a
/// developer wrote - which is what makes a spec-declared constraint and a hand-written one one path
/// rather than two that agree.
/// </para>
/// </remarks>
internal static class OperationParameters {

    internal sealed record Model(string OperationId, string InterfaceName, IReadOnlyList<Member> Members);

    /// <param name="Name">The C# name, matching what the handler's Parameters class declares.</param>
    /// <param name="Type">Its type.</param>
    /// <param name="Attributes">Constraints, already rendered.</param>
    internal sealed record Member(
        string Name, ITypeDefinition Type, IReadOnlyList<ConstraintAttributes.Model> Attributes);

    public static Model? Build(
        OperationModel operation, OpenApiSpecModel spec, string modelsNamespace, PatternRegistry patterns) {
        var members = new List<Member>();
        var constrained = false;

        foreach (var parameter in operation.Parameters) {
            // Header parameters are not bound onto the parameters class, so there is nothing to
            // constrain - see finding 3.6.
            if (parameter.In != "path" && parameter.In != "query") {
                continue;
            }

            var csType = TypeMapper.MapParameterToCSharpType(parameter);
            var attributes = ConstraintAttributes.ForParameter(parameter, patterns);

            constrained |= attributes.Count > 0;

            members.Add(new Member(
                NamingHelper.ToParameterName(parameter.Name),
                TypeMapper.GetTypeDefinition(modelsNamespace, csType, !parameter.IsRequired),
                attributes));
        }

        var bodySchema = BodySchema(operation, spec);

        if (bodySchema != null) {
            var bodyType = TypeDefinition.Get(
                modelsNamespace, NamingHelper.ToPascalCase(bodySchema.Name));

            // [ValidateNested] is what makes the generated validator descend, which is what gives
            // body errors their "body." prefix and distinguishes them from a path parameter of the
            // same name.
            var attributes = bodySchema.Properties.Any(p => p.HasValidationConstraints || p.IsRequired)
                ? new[] { new ConstraintAttributes.Model(
                    ConstraintAttributes.ValidateNested(), System.Array.Empty<string>()) }
                : System.Array.Empty<ConstraintAttributes.Model>();

            constrained |= attributes.Length > 0;

            members.Add(new Member("body", bodyType, attributes));
        }

        return constrained
            ? new Model(
                operation.OperationId,
                "I" + NamingHelper.ToPascalCase(operation.OperationId) + "Parameters",
                members)
            : null;
    }

    private static SchemaModel? BodySchema(OperationModel operation, OpenApiSpecModel spec) {
        if (operation.RequestBodyRef == null) {
            return null;
        }

        var name = TypeMapper.GetRefName(operation.RequestBodyRef);

        return spec.Schemas.FirstOrDefault(s => s.Name == name && s.Kind == SchemaKind.Object);
    }
}
