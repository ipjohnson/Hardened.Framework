using System.Globalization;
using CSharpAuthor;
using CSharpAuthor.Expressions;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Reads a <c>[FromForm]</c> or <c>[FromQueryString]</c> parameter's type into the members the
/// binder assigns one field at a time.
/// </summary>
/// <remarks>
/// <para>
/// <b>A type that binds as one value is left alone.</b> The string converter answers for the
/// scalars, for enums, and at run time for any type an <c>IStringConverter</c> is registered for.
/// The last of those cannot be seen from here, so a type with a static <c>Parse</c> or
/// <c>TryParse</c> is taken to be one value too, and so is anything in a <c>System</c> or
/// <c>Microsoft</c> namespace. Everything else that can be constructed is a model.
/// </para>
/// <para>
/// <b>Names and required-ness follow <see cref="JsonSchemaWriter"/>.</b> A field is named the way
/// the serializer names the member, and a member is required under the rule the document already
/// publishes for the model's schema. The request body a form model publishes can then point at
/// that schema, and the two cannot disagree.
/// </para>
/// <para>
/// <b>The constructor is chosen the way <c>System.Text.Json</c> chooses one:</b> the one marked
/// <c>[JsonConstructor]</c>, otherwise a public parameterless one, otherwise the only public one.
/// </para>
/// </remarks>
public static class BoundModelReader
{
    /// <summary>
    /// The model <paramref name="type"/> binds as, or null when it binds as one value.
    /// </summary>
    /// <param name="renamed">Whether the binding attribute named a field for the parameter.</param>
    public static BoundModel? Read(
        ITypeSymbol type,
        ParameterBindType binding,
        bool renamed,
        Compilation compilation
    )
    {
        if (binding is not (ParameterBindType.Form or ParameterBindType.QueryString))
        {
            return null;
        }

        var underlying = NullableValueType(type);
        var candidate = underlying ?? type;

        if (!IsModel(candidate))
        {
            return null;
        }

        var named = (INamedTypeSymbol)candidate;
        var members = new List<BoundMember>();

        var problem =
            underlying != null || type.NullableAnnotation == NullableAnnotation.Annotated
                ? "it is nullable, and a model bound from fields is constructed whether or not any field was sent"
            : renamed
                ? "a model takes its field names from its members, so the attribute cannot name a field for it"
            : ReadMembers(named, binding, members);

        if (problem != null)
        {
            return new BoundModel(Array.Empty<BoundMember>(), problem, null);
        }

        return new BoundModel(
            members,
            null,
            binding == ParameterBindType.Form
                ? JsonSchemaWriter.Write(named, compilation.Assembly)
                : null
        );
    }

    /// <summary>
    /// The public instance properties of a model, its own before those it inherits.
    /// </summary>
    /// <remarks>
    /// Public because the parameter enum walk needs the same set: an enum member of a query string
    /// model is bound through its wire vocabulary, so the vocabulary has to be collected.
    /// </remarks>
    public static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (
            var current = type;
            current != null && current.SpecialType != SpecialType.System_Object;
            current = current.BaseType
        )
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (
                    property.DeclaredAccessibility != Accessibility.Public
                    || property.IsStatic
                    || property.IsIndexer
                    || !seen.Add(property.Name)
                )
                {
                    continue;
                }

                yield return property;
            }
        }
    }

    /// <summary>Whether a parameter of this type is bound member by member.</summary>
    /// <remarks>
    /// A collection is never a model, even one the application declares. Its public members are
    /// things like <c>Capacity</c>, not fields anyone sends.
    /// </remarks>
    public static bool IsModel(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                TypeKind: TypeKind.Class or TypeKind.Struct,
                IsAbstract: false,
            } named
        && !IsFrameworkType(named)
        && !IsScalar(named)
        && !named.AllInterfaces.Any(candidate =>
            candidate.SpecialType == SpecialType.System_Collections_IEnumerable
        );

    private static string? ReadMembers(
        INamedTypeSymbol type,
        ParameterBindType binding,
        List<BoundMember> members
    )
    {
        var constructors = type
            .InstanceConstructors.Where(constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public
            )
            .ToList();

        var constructor =
            constructors.FirstOrDefault(HasJsonConstructor)
            ?? constructors.FirstOrDefault(candidate => candidate.Parameters.Length == 0)
            ?? (constructors.Count == 1 ? constructors[0] : null);

        if (constructor == null)
        {
            return constructors.Count == 0
                ? $"'{type.Name}' has no public constructor"
                : $"'{type.Name}' has more than one public constructor, and none is marked [JsonConstructor]";
        }

        var properties = Properties(type).ToList();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in constructor.Parameters)
        {
            // Matched without case, the way System.Text.Json matches a constructor parameter to the
            // property it initializes. The property is where a rename is written.
            var property = properties.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
            );

            if (property != null)
            {
                covered.Add(property.Name);
            }

            if (MemberTypeProblem(parameter.Name, parameter.Type, binding) is { } problem)
            {
                return problem;
            }

            var hasDefault = parameter.HasExplicitDefaultValue;

            // A constraint on a positional record parameter stays on the parameter unless it is
            // written with property:, so both are read.
            var declarations =
                property != null
                    ? new ISymbol[] { property, parameter }
                    : new ISymbol[] { parameter };

            members.Add(
                new BoundMember(
                    Value(
                        parameter.Type,
                        parameter.Name,
                        property != null
                            ? JsonSchemaWriter.WireName(property)
                            : JsonSchemaWriter.CamelCase(parameter.Name),
                        !hasDefault && IsRequired(parameter.Type, declarations),
                        hasDefault ? DefaultLiteral(parameter) : null,
                        binding,
                        declarations
                    ),
                    BoundMemberKind.ConstructorArgument
                )
            );
        }

        foreach (var property in properties)
        {
            if (
                covered.Contains(property.Name)
                || property.SetMethod is not { DeclaredAccessibility: Accessibility.Public } setter
                || IsIgnored(property)
            )
            {
                continue;
            }

            if (MemberTypeProblem(property.Name, property.Type, binding) is { } problem)
            {
                return problem;
            }

            // A file member has nothing an initializer could usefully hold, so an absent part is
            // simply an absent file.
            var hasInitializer =
                !IsFile(property.Type) && JsonSchemaWriter.HasInitializer(property);

            BoundMemberKind kind;

            if (setter.IsInitOnly || property.IsRequired)
            {
                // An initializer runs before the object initializer does, and the object
                // initializer cannot leave a member out on a condition. Assigning it anyway would
                // replace the initializer's value with null or zero whenever the field was absent.
                if (hasInitializer)
                {
                    return $"its member '{property.Name}' is init-only and has an initializer, which "
                        + "an absent field would overwrite. Give it a setter, or make it a constructor "
                        + "parameter with a default";
                }

                kind = BoundMemberKind.Initializer;
            }
            else
            {
                kind = hasInitializer ? BoundMemberKind.AssignedWhenSent : BoundMemberKind.Assigned;
            }

            members.Add(
                new BoundMember(
                    Value(
                        property.Type,
                        property.Name,
                        JsonSchemaWriter.WireName(property),
                        !hasInitializer && IsRequired(property.Type, new ISymbol[] { property }),
                        null,
                        binding,
                        new ISymbol[] { property }
                    ),
                    kind
                )
            );
        }

        return members.Count == 0
            ? $"'{type.Name}' has no constructor parameters or settable properties to bind"
            : null;
    }

    private static RequestParameterInformation Value(
        ITypeSymbol type,
        string memberName,
        string wireName,
        bool required,
        string? defaultValue,
        ParameterBindType binding,
        IReadOnlyList<ISymbol> declarations
    )
    {
        var definition = TypeSyntaxExtensions.GetTypeDefinitionFromType(type);

        // GetTypeDefinitionFromType answers string for string?, and the binder chooses between
        // ParseOptional<string?> and ParseOptional<string> by it.
        if (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.Annotated)
        {
            definition = definition.MakeNullable();
        }

        return new RequestParameterInformation(
            definition,
            memberName,
            required,
            defaultValue,
            binding,
            wireName,
            0
        )
        {
            // What the member's constraints say, for a query string model's parameters. The model's
            // validator enforces them; this is the same statement in the document.
            SchemaFacets = declarations
                .Select(SchemaConstraintWriter.FacetsOf)
                .FirstOrDefault(facets => facets != null),
        };
    }

    /// <summary>
    /// The rule <see cref="JsonSchemaWriter"/> writes a model's <c>required</c> list by, for a
    /// member with no default.
    /// </summary>
    private static bool IsRequired(ITypeSymbol type, IReadOnlyList<ISymbol> declarations) =>
        (type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated)
        || (type.IsValueType && NullableValueType(type) == null)
        || declarations.Any(SchemaConstraintWriter.IsRequired);

    private static string? MemberTypeProblem(
        string name,
        ITypeSymbol type,
        ParameterBindType binding
    )
    {
        if (IsFile(type))
        {
            return binding == ParameterBindType.Form
                ? null
                : $"its member '{name}' is a file, and only a multipart form carries one";
        }

        // A type that does not resolve already has the compiler's error, and a second one saying it
        // is not a value would only be noise beside it.
        if (
            type.TypeKind == TypeKind.Error
            || IsScalar(type)
            || (ItemType(type) is { } item && IsScalar(item))
        )
        {
            return null;
        }

        return $"its member '{name}' is a '{type.ToDisplayString()}', and a field carries a value "
            + "rather than an object";
    }

    /// <summary>
    /// Whether the string converter reads the type from one value.
    /// </summary>
    /// <remarks>
    /// The built-in set is <c>StringConverterService</c>'s. A type with its own static <c>Parse</c>
    /// or <c>TryParse</c> is counted as well, because that is the shape of a type an application
    /// registers an <c>IStringConverter</c> for, and a registration is only visible at run time.
    /// </remarks>
    private static bool IsScalar(ITypeSymbol type)
    {
        if (NullableValueType(type) is { } underlying)
        {
            return IsScalar(underlying);
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Char:
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_DateTime:
            case SpecialType.System_Object:
                return true;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        // Base64, which is how a query value carries bytes.
        if (type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte })
        {
            return true;
        }

        if (
            IsFrameworkType(type)
            && type.Name
                is "Guid"
                    or "DateOnly"
                    or "TimeOnly"
                    or "TimeSpan"
                    or "DateTimeOffset"
                    or "Uri"
                    or "StringValues"
        )
        {
            return true;
        }

        return type.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(method =>
                method is { IsStatic: true, DeclaredAccessibility: Accessibility.Public }
                && method.Name is "Parse" or "TryParse"
                && method.Parameters.Length > 0
                && method.Parameters[0].Type.SpecialType == SpecialType.System_String
            );
    }

    /// <summary>
    /// The item type of a collection the binder fills from every value sent under one name, as
    /// <c>CollectionParameter</c> reads it from the emitted type.
    /// </summary>
    private static ITypeSymbol? ItemType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol { Rank: 1 } array)
        {
            return array.ElementType;
        }

        if (
            type is INamedTypeSymbol { TypeArguments.Length: 1 } named
            && named.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic"
            && named.Name
                is "List"
                    or "IList"
                    or "ICollection"
                    or "IEnumerable"
                    or "IReadOnlyList"
                    or "IReadOnlyCollection"
        )
        {
            return named.TypeArguments[0];
        }

        return null;
    }

    /// <summary>Whether a member is <c>IFormFile</c> or a collection of it.</summary>
    private static bool IsFile(ITypeSymbol type) =>
        FormFileType.Is(type) || (ItemType(type) is { } item && FormFileType.Is(item));

    private static bool IsFrameworkType(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";

        return ns == "System"
            || ns.StartsWith("System.", StringComparison.Ordinal)
            || ns == "Microsoft"
            || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    private static ITypeSymbol? NullableValueType(ITypeSymbol type) =>
        type
            is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            } nullable
            ? nullable.TypeArguments[0]
            : null;

    private static bool HasJsonConstructor(IMethodSymbol constructor) =>
        constructor
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.Name == "JsonConstructorAttribute"
                && attribute.AttributeClass.ContainingNamespace?.ToDisplayString()
                    == "System.Text.Json.Serialization"
            );

    /// <summary>
    /// Whether <c>[JsonIgnore]</c> keeps the serializer from reading the member.
    /// </summary>
    /// <remarks>
    /// A member the JSON body cannot set should not be settable from a form either. Only the
    /// default condition and <c>Always</c> stop a read; the <c>WhenWriting</c> conditions are about
    /// the response.
    /// </remarks>
    private static bool IsIgnored(IPropertySymbol property) =>
        property
            .GetAttributes()
            .Any(attribute =>
                attribute.AttributeClass?.Name == "JsonIgnoreAttribute"
                && attribute.AttributeClass.ContainingNamespace?.ToDisplayString()
                    == "System.Text.Json.Serialization"
                && attribute
                    .NamedArguments.Where(argument => argument.Key == "Condition")
                    .All(argument => argument.Value.Value is 1)
            );

    /// <summary>
    /// A constructor parameter's default, as the C# the binder passes to <c>ParseWithDefault</c>.
    /// </summary>
    /// <remarks>
    /// Written from the constant rather than copied from the syntax: the syntax can name a constant
    /// that is in scope where the model is declared and not where the binder is.
    /// </remarks>
    private static string DefaultLiteral(IParameterSymbol parameter)
    {
        var value = parameter.ExplicitDefaultValue;
        var type = NullableValueType(parameter.Type) ?? parameter.Type;

        if (value == null)
        {
            return parameter.Type.IsReferenceType || NullableValueType(parameter.Type) != null
                ? "null"
                : "default";
        }

        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum })
        {
            return "(global::"
                + type.ToDisplayString()
                + ")("
                + System.Convert.ToString(value, CultureInfo.InvariantCulture)
                + ")";
        }

        return value switch
        {
            string text => CSharpText.StringLiteral(text),
            char character => CSharpText.CharLiteral(character),
            bool flag => flag ? "true" : "false",
            float number => CSharpText.SingleLiteral(number),
            double number => CSharpText.DoubleLiteral(number),
            decimal number => CSharpText.DecimalLiteral(number),
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => "default",
        };
    }
}
