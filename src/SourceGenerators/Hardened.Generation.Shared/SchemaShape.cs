using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Generation;

namespace Hardened.Generation;

/// <summary>
/// How a schema's properties are divided between a record's constructor and its body.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SchemaEmitter"/> writes the record and <c>JsonTypeInfoEmitter</c> writes the metadata
/// describing that same record to the AOT resolver, so the two have to agree on the split exactly.
/// The resolver's <c>ObjectWithParameterizedConstructorCreator</c> casts a positional argument array
/// and its <c>ConstructorParameterMetadataInitializer</c> assigns positions by index; if either
/// disagrees with the constructor that was actually emitted, arguments land in the wrong parameters
/// and the mismatch shows up as a cast failure at run time rather than as a build error.
/// </para>
/// <para>
/// Derived from one place for that reason. It was previously an <c>OrderByDescending</c> expression
/// duplicated in both files, which was already noted as a coupling to watch.
/// </para>
/// </remarks>
internal static class SchemaShape {

    /// <summary>
    /// The properties the record declares positionally, in the order it declares them.
    /// </summary>
    /// <remarks>
    /// Required first, because C# will not take an optional parameter before a required one. A
    /// derived record passes its base's arguments in this same order, which is the other reason the
    /// ordering cannot live at a call site.
    /// </remarks>
    public static List<PropertyModel> Constructor(SchemaModel schema) =>
        schema.Properties
            .Where(property => property.IsConstructorParameter)
            // Ordered by whether the parameter carries a default, not by whether the description
            // calls the property required. A read-only property is required in a response and
            // optional in C#, so ordering by the description put an optional parameter before a
            // required one - CS1737, and a generated file that does not compile.
            .OrderByDescending(property => !property.HasDefault)
            .ToList();

    /// <summary>
    /// The properties the record declares in its body, as init-only members.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>readOnly</c> ones. They stay out of the constructor so that deserialization has no way
    /// to populate them, and stay assignable through <c>init</c> so the application still can:
    /// <c>body with { Id = NewId() }</c> uses the C# initializer, which the JSON resolver knows
    /// nothing about.
    /// </para>
    /// <para>
    /// A property a base already declares is excluded. <c>allOf</c> merges a base's properties into
    /// the derived schema, so both would otherwise declare it - harmless for a positional parameter,
    /// which the derived record forwards to the base, but a redeclared member hides the inherited one
    /// (CS0108, an error here under <c>ContinuousIntegrationBuild</c>).
    /// </para>
    /// </remarks>
    public static List<PropertyModel> Members(
        SchemaModel schema, IReadOnlyList<SchemaModel>? allSchemas) {
        var inherited = Base(schema, allSchemas);

        return schema.Properties
            .Where(property => !property.IsConstructorParameter)
            .Where(property => inherited?.Properties.Any(
                declared => declared.Name == property.Name) != true)
            .ToList();
    }

    /// <summary>
    /// The properties this type declares itself, rather than forwarding to a base.
    /// </summary>
    /// <remarks>
    /// A positional record passes an inherited property straight to the base constructor, so it
    /// never becomes a member of the derived type - and a constraint on it is the base's to check.
    /// Bitbucket's <c>DeploymentEnvironment</c> derives from a schema carrying the only constrained
    /// property, which made it look constrained here and unconstrained to the validation generator:
    /// one emitted a [ValidateNested] and the other declined to generate the validator it named.
    /// </remarks>
    public static List<PropertyModel> Declared(
        SchemaModel schema, IReadOnlyList<SchemaModel>? allSchemas) {
        var inherited = Base(schema, allSchemas);

        return schema.Properties
            .Where(property => inherited?.Properties.Any(
                declared => declared.Name == property.Name) != true)
            .ToList();
    }

    /// <summary>The schema this one derives from, where it declares one and it is resolvable.</summary>
    public static SchemaModel? Base(SchemaModel schema, IReadOnlyList<SchemaModel>? allSchemas) {
        if (schema.BaseRef == null) {
            return null;
        }

        var baseName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(schema.BaseRef));

        return allSchemas?.FirstOrDefault(
            candidate => NamingHelper.ToPascalCase(candidate.Name) == baseName);
    }
}
