using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator;
using Hardened.Idl.Models;
using Hardened.Idl;

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
        OperationModel operation, ServiceSpecModel spec, string modelsNamespace, PatternRegistry patterns) {
        var members = new List<Member>();
        var constrained = false;

        // Enums the document declares, by the name their generated type carries. A parameter typed
        // as one of these is already restricted to its members, and it is not a reference type -
        // both of which change which constraints can be emitted against it.
        var enumTypes = new HashSet<string>();

        foreach (var schema in spec.Schemas) {
            if (schema.Kind == SchemaKind.Enum) {
                enumTypes.Add(NamingHelper.ToPascalCase(schema.Name));
            }
        }

        foreach (var parameter in operation.Parameters) {
            var csType = TypeMapper.MapParameterToCSharpType(parameter);
            var isEnumType = enumTypes.Contains(csType);

            // Suppressed where the C# type already guarantees presence, exactly as SchemaEmitter
            // has always done for properties. A required integer path parameter is the common case.
            var emitRequired = parameter.ConstrainedAsRequired &&
                               !TypeMapper.IsNonNullableValueType(csType, spec.Schemas);

            var attributes = ConstraintAttributes.ForParameter(
                parameter, emitRequired, csType, patterns);

            constrained |= attributes.Count > 0;

            members.Add(new Member(
                parameter.MemberName,
                TypeMapper.GetTypeDefinition(modelsNamespace, csType, parameter.IsCSharpNullable),
                attributes));
        }

        var bodySchema = BodySchema(operation, spec);

        if (bodySchema != null) {
            var bodyType = TypeDefinition.Get(
                modelsNamespace, NamingHelper.ToPascalCase(bodySchema.Name));

            // [ValidateNested] is what makes the generated validator descend, which is what gives
            // body errors their "body." prefix and distinguishes them from a path parameter of the
            // same name.
            // Constrained excludes readOnly properties, whose constraints are never emitted - a
            // validator that descends into a body with nothing to check is dead code.
            //
            // Asked by building the body's attributes rather than by re-deriving the rule. A body
            // whose only constraint was `required` on a non-nullable value type now gets no
            // attributes at all, so no validator is generated for it - and a [ValidateNested]
            // naming a validator that does not exist is CS0234 in a generated file. Both answers
            // have to come from one place to stay in step.
            var attributes = bodySchema.Properties.Any(
                property => property.Constrained &&
                            PropertyAttributes(property, spec, patterns).Count > 0)
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

    /// <summary>
    /// The attributes a property would carry, by the same rules the model emitter applies.
    /// </summary>
    private static IReadOnlyList<ConstraintAttributes.Model> PropertyAttributes(
        PropertyModel property, ServiceSpecModel spec, PatternRegistry patterns) {
        var csType = TypeMapper.MapPropertyToCSharpType(property);

        return ConstraintAttributes.ForProperty(
            property,
            property.ConstrainedAsRequired && !TypeMapper.IsNonNullableValueType(csType, spec.Schemas),
            patterns,
            csType);
    }

    private static SchemaModel? BodySchema(OperationModel operation, ServiceSpecModel spec) {
        if (operation.RequestBodyRef == null) {
            return null;
        }

        var name = TypeMapper.GetRefName(operation.RequestBodyRef);

        return spec.Schemas.FirstOrDefault(s => s.Name == name && s.Kind == SchemaKind.Object);
    }
}
