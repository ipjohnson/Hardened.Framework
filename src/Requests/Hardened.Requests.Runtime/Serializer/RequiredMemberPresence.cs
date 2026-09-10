using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.Requests.Runtime.Serializer;

/// <summary>
/// Makes the deserializer refuse a body that omits a member the published document calls required.
/// </summary>
/// <remarks>
/// <para>
/// <b>The document already said so, and nothing enforced it.</b> A schema's <c>required</c> array
/// carries a non-nullable reference type because the author said so, and the validator was built
/// from <c>[Required]</c> alone - so <c>record NewTodo(string Title)</c> published
/// <c>required: ["title"]</c> and answered 201 to <c>{}</c> with a null title in a domain whose C#
/// type says it cannot be there. Two generators read one declaration and reached opposite
/// conclusions.
/// </para>
/// <para>
/// <b>Reference types only, which is where the declaration is.</b> The document also publishes a
/// non-nullable <em>value</em> type as required, on different grounds: not that the author demanded
/// it, but that C# serialization cannot omit one, which is a fact about what a response always
/// contains. Reading that as a demand on a caller would make <c>int Age</c> mandatory in every body
/// that declares one, from a member whose author never chose anything - <c>int?</c> and <c>= 0</c>
/// being the only ways C# has to say otherwise. A value type the author does mean to demand is
/// <c>HRDV003</c>'s subject, and its remedies are <c>required</c> and <c>[JsonRequired]</c>.
/// </para>
/// <para>
/// <b>Presence is the deserializer's job; content is the validator's.</b> Absence is the one thing a
/// validator cannot see - an omitted <c>int</c> is indistinguishable from <c>0</c> once the model is
/// built - and it is the one thing the reader knows for certain. So the reader answers "was it
/// sent", <c>[Required]</c> and the rest answer "is it acceptable", and neither asks the other's
/// question.
/// </para>
/// <para>
/// The rule here is <c>JsonSchemaWriter</c>'s, member for member, because a rule stated twice is a
/// rule that drifts: a member with a constructor default is excluded in both places, and an
/// assembly without nullable annotations enables neither. The one thing this cannot see is a type
/// whose annotations the trimmer removed, which is why it is installed by the reflection-based
/// deserializer alone - the source-generated resolvers carry <c>IsRequired</c> from their own
/// generator, decided at build time from the same contract.
/// </para>
/// <para>
/// Costs nothing per request. A modifier runs once per type, when
/// <see cref="JsonSerializerOptions"/> first builds that type's metadata, and the metadata is cached
/// from then on.
/// </para>
/// </remarks>
internal static class RequiredMemberPresence {
    private const string Reason =
        "Reads a model's nullable annotations by reflection. The source-generated resolvers carry " +
        "IsRequired from their own generator instead.";

    /// <summary>
    /// Adds the rule to <paramref name="options"/>, over whatever resolvers it already carries.
    /// </summary>
    /// <remarks>
    /// The chain is composed and re-added rather than assigned through
    /// <see cref="JsonSerializerOptions.TypeInfoResolver"/>: the property and the chain are two views
    /// of one thing, and setting the property replaces what the caller spent a constructor building.
    /// </remarks>
    [RequiresUnreferencedCode(Reason)]
    public static JsonSerializerOptions Enforce(JsonSerializerOptions options) {
        var chain = new IJsonTypeInfoResolver[options.TypeInfoResolverChain.Count];

        options.TypeInfoResolverChain.CopyTo(chain, 0);
        options.TypeInfoResolverChain.Clear();
        options.TypeInfoResolverChain.Add(
            JsonTypeInfoResolver.WithAddedModifier(JsonTypeInfoResolver.Combine(chain), Require));

        return options;
    }

    [RequiresUnreferencedCode(Reason)]
    private static void Require(JsonTypeInfo typeInfo) {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) {
            return;
        }

        NullabilityInfoContext? nullability = null;

        foreach (var property in typeInfo.Properties) {
            // Already required, by the `required` modifier, [JsonRequired], or a generator that
            // decided the same thing from a contract.
            if (property.IsRequired || property.IsExtensionData) {
                continue;
            }

            if (property.AttributeProvider is not PropertyInfo member) {
                continue;
            }

            // A member the reader cannot populate cannot be demanded of a caller. A get-only
            // property is either constructor-bound - in which case the parameter below is what
            // carries it - or it is not deserialized at all, and requiring one of those would
            // refuse every request.
            if (property.Set == null && Parameter(typeInfo.Type, member.Name) == null) {
                continue;
            }

            // The caller may omit a member the server fills in. This is the one exclusion the
            // document makes too.
            if (Parameter(typeInfo.Type, member.Name) is { HasDefaultValue: true }) {
                continue;
            }

            // A value the server owns is not one to demand of a caller. This is OpenAPI's readOnly,
            // which the specification-first side excludes from required for the same reason -
            // ConstrainedAsRequired reads IsReadOnly.
            if (member.GetCustomAttribute<ResponseOnlyAttribute>() != null) {
                continue;
            }

            if (!DeclaredAlwaysPresent(member, ref nullability)) {
                continue;
            }

            property.IsRequired = true;
        }
    }

    /// <summary>
    /// Whether the author declared this member always present: a reference type the nullable
    /// context says cannot be null.
    /// </summary>
    /// <remarks>
    /// <see cref="NullabilityInfo.WriteState"/> rather than <c>ReadState</c>, because reading a body
    /// writes into the member. An assembly compiled without nullable annotations answers
    /// <see cref="NullabilityState.Unknown"/> for every reference type, and nothing is required there
    /// - which is also what the document says for it, since the annotation it reads is absent.
    /// </remarks>
    [RequiresUnreferencedCode(Reason)]
    private static bool DeclaredAlwaysPresent(
        PropertyInfo member, ref NullabilityInfoContext? nullability) {
        if (member.PropertyType.IsValueType) {
            return false;
        }

        nullability ??= new NullabilityInfoContext();

        return nullability.Create(member).WriteState == NullabilityState.NotNull;
    }

    /// <summary>
    /// The constructor parameter of that name, or null.
    /// </summary>
    /// <remarks>
    /// By name across every instance constructor, which is the test <c>JsonSchemaWriter.DefaultOf</c>
    /// makes. Ordinal and case-sensitive: a positional record's parameter and its property are one
    /// name, and a hand-written constructor whose parameter is spelled differently is not that
    /// property's default in the document either.
    /// </remarks>
    [RequiresUnreferencedCode(Reason)]
    private static ParameterInfo? Parameter(Type type, string name) {
        foreach (var constructor in type.GetConstructors()) {
            foreach (var parameter in constructor.GetParameters()) {
                if (string.Equals(parameter.Name, name, StringComparison.Ordinal)) {
                    return parameter;
                }
            }
        }

        return null;
    }
}
