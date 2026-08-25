using System;
using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.Idl.Validation;
using Hardened.Generation.Models;
using Hardened.Idl;
using Hardened.Generation;

namespace Hardened.Idl.Emitters;

/// <summary>
/// One schema, as a record or an enum.
/// </summary>
/// <remarks>
/// Both kinds arrive as a <see cref="SchemaModel"/>, so the dispatch belongs here rather than in
/// every caller. These were two emitters when each one produced a file of its own; the split was
/// along output files rather than along input.
/// </remarks>
internal static class SchemaEmitter {

    /// <summary>
    /// Adds the schema's type to <paramref name="container"/> and returns it, so the caller can
    /// decide anything that is not the type's own business - see <see cref="Coverage"/>.
    /// </summary>
    public static IOutputComponent? Emit(
        IConstructContainer container, SchemaModel schema, string modelsNamespace, PatternRegistry patterns,
        IReadOnlyList<SchemaModel>? allSchemas = null) =>
        schema.Kind switch {
            SchemaKind.Object => EmitRecord(container, schema, modelsNamespace, patterns, allSchemas),
            SchemaKind.Enum => EmitEnumWithConverter(container, schema, modelsNamespace),
            SchemaKind.OneOf => EmitOneOf(container, schema, modelsNamespace, allSchemas),
            _ => null,
        };

    /// <summary>
    /// The enum and the converter that maps it to the values the description declares.
    /// </summary>
    private static IOutputComponent EmitEnumWithConverter(
        IConstructContainer container, SchemaModel schema, string modelsNamespace) {
        var type = EmitEnum(container, schema, modelsNamespace);

        EnumConverterEmitter.Emit(container, schema, modelsNamespace);

        return type;
    }

    /// <summary>
    /// The choice type and the converter that resolves it, which are one thing in two declarations.
    /// </summary>
    private static IOutputComponent EmitOneOf(
        IConstructContainer container, SchemaModel schema, string modelsNamespace,
        IReadOnlyList<SchemaModel>? allSchemas) {
        var type = OneOfEmitter.Emit(container, schema, modelsNamespace);

        OneOfConverterEmitter.Emit(
            container, schema, modelsNamespace,
            allSchemas ?? System.Array.Empty<SchemaModel>());

        return type;
    }

    /// <summary>
    /// A positional record, or a declaration-only one when the schema carries no properties.
    /// </summary>
    private static ClassDefinition EmitRecord(
        IConstructContainer container, SchemaModel schema, string modelsNamespace, PatternRegistry patterns,
        IReadOnlyList<SchemaModel>? allSchemas) {
        var record = container.AddClass(NamingHelper.ToPascalCase(schema.Name));

        record.TypeKeyword = ClassKeyword.Record;
        record.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        record.Comment = DocComment.Format(schema.Description);

        if (schema.IsDeprecated) {
            Deprecation.Apply(record);
        }

        EmitBaseType(record, schema, modelsNamespace, allSchemas);

        var parameters = SchemaShape.Constructor(schema);
        var members = SchemaShape.Members(schema, allSchemas);

        // A record with a body cannot be terminated with a semicolon - the two are alternative
        // declaration forms, and TerminateWithSemicolon writes the signature and stops, so members
        // set on it would never be written at all.
        record.TerminateWithSemicolon = members.Count == 0;

        // No parameters means no parameter list at all - "record Empty;" rather than "record
        // Empty();". The two are different declarations, and the second gives the type a constructor
        // the spec did not ask for.
        if (parameters.Count > 0) {
            var constructor = record.AddConstructor();
            constructor.IsPrimary = true;

            foreach (var property in parameters) {
                EmitConstructorParameter(constructor, property, modelsNamespace, patterns, allSchemas);
            }
        }

        foreach (var property in members) {
            EmitInitOnlyMember(record, property, modelsNamespace);
        }

        return record;
    }

    /// <summary>One property, as a positional record parameter.</summary>
    private static void EmitConstructorParameter(
        ConstructorDefinition constructor, PropertyModel property, string modelsNamespace,
        PatternRegistry patterns, IReadOnlyList<SchemaModel>? allSchemas) {
        var csType = TypeMapper.MapPropertyToCSharpType(property);
        var typeDefinition = TypeMapper.GetTypeDefinition(modelsNamespace, csType, property.IsCSharpNullable);

        var parameter = constructor.AddParameter(
            typeDefinition, property.MemberName);

        parameter.Comment = DocComment.Format(property.Description);

        if (property.HasDefault) {
            // The spec's own default where it has a constant form, and the type's otherwise.
            var literal = DefaultLiteral.Format(property.Default, csType) ?? "default";

            parameter.DefaultValue = new CodeOutputComponent(literal) { Indented = false };
        }

        // property:, because a positional record's parameter and the property it declares are one
        // syntactic position. Without the target the attribute stays on the parameter, where a
        // generator reading properties never sees it - which is what VM0051 warns about.
        EmitJsonPropertyName(parameter, property).Target = "property";
        if (EmitDirection(parameter, property) is { } direction) {
            direction.Target = "property";
        }

        // Required, except where the type already guarantees it - see
        // TypeMapper.IsNonNullableValueType.
        var emitRequired = property.ConstrainedAsRequired &&
                           !TypeMapper.IsNonNullableValueType(csType, allSchemas);

        // The other half of that sentence. A required member of a value type gets no [Required] -
        // the validation generator would emit `value.x is null` against an int, which is CS0037 -
        // so absence used to become default(T) in silence: an omitted enum became its first
        // declared member and the API answered 201 with a value the caller never sent.
        //
        // [JsonRequired] rather than a nullable member, so the model's shape is unchanged and a
        // handler still reads an int. The deserializer is the only layer that still knows the
        // member was absent, and this is how it is told to care.
        //
        // Emitted here *and* as IsRequired in JsonTypeInfoEmitter, because the two deserializers
        // read different things: the reflection-based one reads this attribute, and the
        // source-generated resolver builds JsonPropertyInfo by hand and never sees it. readOnly is
        // enforced twice for the same reason - a null Setter there, [ResponseOnly] here.
        if (property.ConstrainedAsRequired &&
            TypeMapper.IsNonNullableValueType(csType, allSchemas)) {
            parameter.AddAttribute(
                TypeDefinition.Get("System.Text.Json.Serialization", "JsonRequiredAttribute"))
                .Target = "property";
        }

        foreach (var constraint in ConstraintAttributes.ForProperty(
                     property, emitRequired, patterns, csType)) {
            ValidationEmitter.Apply(parameter, constraint).Target = "property";
        }

        // Descend into a value that carries constraints of its own.
        //
        // This was emitted on an operation's body parameter and nowhere else, so a constraint on
        // an array's items or on a nested object was generated, registered in DI, and never
        // called: `lines: [{quantity: 0}]` against `minimum: 1` was accepted and the order placed.
        // Only the mark was missing - ValidationModules already validates each element of a
        // collection and each value of a dictionary, indexed, once told to.
        //
        // Guarded rather than unconditional: naming a validator the validation generator declined
        // to emit is CS0234 in a file nobody can edit. NestedValidation is where both sides ask.
        if (NestedValidation.Descends(property, allSchemas, patterns)) {
            ValidationEmitter.Apply(
                parameter,
                new ConstraintAttributes.Model(
                    ConstraintAttributes.ValidateNested(), System.Array.Empty<string>()))
                .Target = "property";
        }
    }

    /// <summary>
    /// One <c>readOnly</c> property, as an init-only member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Outside the constructor is what makes it read-only on the wire: deserialization populates a
    /// record through its constructor, and <c>JsonTypeInfoEmitter</c> gives the property no setter,
    /// so a client sending the value has it discarded. The application still assigns it, through
    /// <c>init</c> - <c>body with { Id = NewId() }</c> - because that is a C# initializer and the
    /// JSON resolver plays no part in it.
    /// </para>
    /// <para>
    /// Non-nullable properties are initialized to <c>default!</c>. The value is null between
    /// construction and the <c>with</c> that populates it, which is a window only the deserializer
    /// sees; typing it nullable instead would push a null check onto every consumer reading a field
    /// the specification says is always present in a response.
    /// </para>
    /// <para>
    /// No validation constraints - see <see cref="PropertyModel.Constrained"/>.
    /// </para>
    /// </remarks>
    private static void EmitInitOnlyMember(
        ClassDefinition record, PropertyModel property, string modelsNamespace) {
        var csType = TypeMapper.MapPropertyToCSharpType(property);
        var typeDefinition = TypeMapper.GetTypeDefinition(modelsNamespace, csType, property.IsCSharpNullable);

        var member = record.AddProperty(typeDefinition, property.MemberName);

        member.Modifiers |= ComponentModifier.Public;
        member.Set = new PropertyMethodDefinition { IsInit = true };
        member.Comment = DocComment.Format(property.Description);

        if (!property.IsCSharpNullable) {
            member.DefaultValue = new CodeOutputComponent("default!") { Indented = false };
        }

        EmitJsonPropertyName(member, property);
        EmitDirection(member, property);
    }

    /// <summary>
    /// The base a derived record inherits, and the arguments it passes to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A derived schema carries its base's properties as well as its own - <c>allOf</c> merges them
    /// - so the record declares all of them positionally and forwards the base's share. That is the
    /// standard shape for record inheritance: <c>record Dog(string PetType, string Name, string
    /// Breed) : Pet(PetType, Name)</c> declares <c>Breed</c> and inherits the other two rather than
    /// redeclaring them.
    /// </para>
    /// <para>
    /// The arguments are ordered by the base's declaration order, not this schema's. The two agree
    /// on membership but not necessarily on order, since each sorts its own required properties
    /// first.
    /// </para>
    /// </remarks>
    private static void EmitBaseType(
        ClassDefinition record, SchemaModel schema, string modelsNamespace,
        IReadOnlyList<SchemaModel>? allSchemas) {
        if (schema.BaseRef == null) {
            return;
        }

        var baseName = NamingHelper.ToPascalCase(TypeMapper.GetRefName(schema.BaseRef));
        var baseType = TypeDefinition.Get(modelsNamespace, baseName);

        var baseSchema = SchemaShape.Base(schema, allSchemas);

        // The base's own constructor parameters, which is not the same as its property list: a
        // readOnly property is a member the derived record inherits rather than an argument it
        // passes.
        var inherited = baseSchema == null
            ? new List<PropertyModel>()
            : SchemaShape.Constructor(baseSchema);

        if (inherited.Count == 0) {
            record.AddBaseType(baseType);

            return;
        }

        var arguments = new List<IOutputComponent>();

        foreach (var property in inherited) {
            arguments.Add(
                new CodeOutputComponent(property.MemberName) { Indented = false });
        }

        record.AddBaseType(baseType, arguments.ToArray());
    }

    /// <summary>
    /// Pins the wire name to what the spec said, rather than to whatever a naming policy makes of
    /// the C# name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted for every property, not only where the two differ. The name in the document is the
    /// contract; deriving it from the C# identifier means the contract depends on a
    /// <c>JsonSerializerOptions</c> setting an application is free to change.
    /// </para>
    /// <para>
    /// The two serialization paths disagreed without it. <c>JsonTypeInfoEmitter</c> writes
    /// <c>PropertyName</c> straight from the spec, so the AOT resolver was always right, while
    /// <c>SystemTextJsonResponseSerializer</c> runs reflection over <c>JsonSerializerDefaults.Web</c>
    /// and camel-cases the C# name. A spec property <c>random_number</c> became <c>RandomNumber</c>,
    /// which the resolver reported as <c>random_number</c> and reflection as <c>randomNumber</c> -
    /// one spec, two wire formats, decided by which serializer an application happened to register.
    /// Nothing caught it because every spec in this repository is already camelCase, where the round
    /// trip is lossless.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// The caller sets <c>Target</c>: a positional record parameter needs <c>property:</c> to reach
    /// the property it declares, while a member declared in the body is already the property.
    /// </remarks>
    /// <summary>
    /// Which direction the description says the value travels, where it says so.
    /// </summary>
    /// <remarks>
    /// Documentation rather than a missing accessor. Withholding one was how this used to be
    /// expressed, and it made the value unreachable in the direction the contract does allow - a
    /// response's <c>created_at</c> was dropped on the way in, a request's password could not be
    /// written on the way out. See <c>ResponseOnlyAttribute</c>.
    /// </remarks>
    private static AttributeDefinition? EmitDirection(
        BaseOutputComponent target, PropertyModel property) {
        if (!property.IsReadOnly && !property.IsWriteOnly) {
            return null;
        }

        return target.AddAttribute(
            TypeDefinition.Get(
                "Hardened.Requests.Abstract.Attributes",
                property.IsReadOnly ? "ResponseOnlyAttribute" : "RequestOnlyAttribute"));
    }

    private static AttributeDefinition EmitJsonPropertyName(
        BaseOutputComponent parameter, PropertyModel property) =>
        parameter.AddAttribute(
            TypeDefinition.Get("System.Text.Json.Serialization", "JsonPropertyNameAttribute"),
            new CodeOutputComponent($"\"{property.Name}\"") { Indented = false });

    private static EnumDefinition EmitEnum(
        IConstructContainer container, SchemaModel schema, string modelsNamespace) {
        var enumDefinition = container.AddEnum(NamingHelper.ToPascalCase(schema.Name));

        enumDefinition.Modifiers |= ComponentModifier.Public;
        enumDefinition.Comment = DocComment.Format(schema.Description);

        if (schema.IsDeprecated) {
            Deprecation.Apply(enumDefinition);
        }

        // The wire values are the description's; the member names are C#. The generated converter is
        // what keeps the two in step, so it is not optional decoration.
        //
        // It names that converter rather than JsonStringEnumConverter, which was here before and is
        // wrong whenever the two differ: JsonStringEnumConverter writes the C# member name, so a
        // value declared as "dog" left as "Dog". The generated converter was already emitted beside
        // every enum and registered in the resolver - but an attribute on the type wins over a
        // converter the resolver supplies, so the resolver's was never consulted.
        //
        // Unexercised until a Smithy model arrived: an OpenAPI enum declared inline on a property
        // maps to string and generates no enum at all, and the OpenAPI fixture has only those. Every
        // Smithy enum is a named shape, so every one of them hit it.
        enumDefinition.AddAttribute(
            TypeDefinition.Get("System.Text.Json.Serialization", "JsonConverterAttribute"),
            new CodeOutputComponent(
                    $"typeof(global::{modelsNamespace}.{EnumConverterEmitter.ConverterName(schema.Name)})")
                { Indented = false });

        // An integer enum's members are its declared numbers, so the C# member carries the value it
        // stands for rather than a position. A string enum's numbering is meaningless and stays
        // implicit, which keeps every existing generated enum byte-identical.
        var underlying = EnumWireForm.UnderlyingType(schema);

        if (underlying != null) {
            enumDefinition.BaseType = TypeDefinition.Get("System", "Int64");
        }

        var numeric = EnumWireForm.IsNumeric(schema);

        // Allocated, not derived - see NameAllocator. Two wire values can reach C# as one member
        // name, and deciding that here would be deciding it in one of the places that used to.
        var members = EnumWireForm.MemberNames(schema);

        for (var index = 0; index < members.Count; index++) {
            // The literal itself, not a component wrapping it - AddValue renders the argument with
            // ToString(), so a component lands in the output as its own type name.
            if (numeric) {
                enumDefinition.AddValue(members[index], schema.EnumValues[index]);
            }
            else {
                enumDefinition.AddValue(members[index]);
            }
        }

        return enumDefinition;
    }
}
