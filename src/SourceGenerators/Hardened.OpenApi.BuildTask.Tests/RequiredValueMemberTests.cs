using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Catching a required member the caller did not send.
/// </summary>
/// <remarks>
/// <para>
/// The defect these were written for: a missing required member of a value type silently became
/// <c>default(T)</c>. <c>POST /products</c> omitting <c>category</c> answered <b>201</b> with
/// <c>"category":"tools"</c> - the enum's first declared member - so the API invented data and
/// reported success. An omitted integer became 0, caught only where some unrelated constraint
/// happened to reject 0.
/// </para>
/// <para>
/// <c>[Required]</c> cannot cover a value type: the validation generator emits
/// <c>value.x is null</c>, which is CS0037 against an <c>int</c>, so the constraint is correctly
/// suppressed and nothing replaced it. The deserializer is the only layer that still knows the
/// member was absent.
/// </para>
/// <para>
/// A reference member is marked here too, though <c>[Required]</c> can check it. Presence is one
/// question, and asking it in two layers meant whichever answered first hid the other: a body
/// omitting an integer and three strings was told about the integer, and about the strings one
/// round trip later. The validator keeps every other constraint, including the null a
/// <c>required</c> member is still not allowed to carry.
/// </para>
/// </remarks>
public class RequiredValueMemberTests {

    private static string Emit(params PropertyModel[] properties) =>
        EmitterHarness.JsonTypeInfo(
            [new SchemaModel {
                Name = "Product",
                Kind = SchemaKind.Object,
                Required = properties.Where(p => p.IsRequired).Select(p => p.Name).ToList(),
                Properties = properties.ToList()
            }],
            "depot");

    private static string EmitWithEnum(params PropertyModel[] properties) =>
        EmitterHarness.JsonTypeInfo(
            [
                new SchemaModel {
                    Name = "Product",
                    Kind = SchemaKind.Object,
                    Required = properties.Where(p => p.IsRequired).Select(p => p.Name).ToList(),
                    Properties = properties.ToList()
                },
                new SchemaModel {
                    Name = "Category",
                    Kind = SchemaKind.Enum,
                    EnumValues = ["tools", "toys"]
                }
            ],
            "depot");

    /// <summary>
    /// A required integer is marked, so absence is a 400 rather than a zero.
    /// </summary>
    [Fact]
    public void ARequiredIntegerIsMarkedRequired() {
        var result = Emit(new PropertyModel {
            Name = "unitPriceCents", Type = "integer", IsRequired = true
        });

        Assert.Contains("Required(JsonMetadataServices.CreatePropertyInfo", result);
        Assert.Contains("property.IsRequired = true", result);
    }

    /// <summary>
    /// The enum case, which is the one that invented data: nothing rejects an enum's first member,
    /// so the request succeeded and stored a category the caller never sent.
    /// </summary>
    [Fact]
    public void ARequiredEnumIsMarkedRequired() {
        var result = EmitWithEnum(new PropertyModel {
            Name = "category", Ref = "#/components/schemas/Category", IsRequired = true
        });

        Assert.Contains("Required(JsonMetadataServices.CreatePropertyInfo", result);
    }

    /// <summary>
    /// The marked property gets a setter that does nothing, because System.Text.Json refuses
    /// <c>IsRequired</c> without one - and these records are filled through their constructor, whose
    /// init-only members cannot be assigned through a delegate.
    /// </summary>
    /// <remarks>
    /// Pinned because it looks removable. Dropping it is
    /// <c>InvalidOperationException: JsonPropertyInfo 'x' ... is marked required but does not
    /// specify a setter</c> - thrown on the first request rather than at build time.
    /// </remarks>
    [Fact]
    public void AMarkedPropertyCarriesANoOpSetter() {
        var result = Emit(new PropertyModel {
            Name = "unitPriceCents", Type = "integer", IsRequired = true
        });

        Assert.Contains("Setter = static (obj, value) => { },", result);
    }

    /// <summary>
    /// A required reference type is marked too.
    /// </summary>
    /// <remarks>
    /// It was left to <c>[Required]</c> alone, because the validator aggregates and the reader was
    /// thought to stop at the first fault. The reader aggregates missing members as well, and the
    /// split meant a body missing one of each kind reported only the value type - the reference
    /// members came back a round trip later, from a validator that never ran the first time.
    /// </remarks>
    [Fact]
    public void ARequiredStringIsMarkedRequired() {
        var result = Emit(new PropertyModel { Name = "sku", Type = "string", IsRequired = true });

        Assert.Contains("Required(JsonMetadataServices.CreatePropertyInfo", result);
        Assert.Contains("property.IsRequired = true", result);
    }

    /// <summary>
    /// And still carries <c>[Required]</c>, which is a different question: <c>{"sku":null}</c> sent
    /// the member, so the reader is satisfied and the validator is what refuses the null.
    /// </summary>
    [Fact]
    public void ARequiredStringStillCarriesTheValidatorsConstraint() {
        Assert.Contains(
            "[property: Required]",
            EmitterHarness.Schema(new SchemaModel {
                Name = "Product",
                Kind = SchemaKind.Object,
                Required = ["sku"],
                Properties = [new PropertyModel { Name = "sku", Type = "string", IsRequired = true }]
            }));
    }

    /// <summary>
    /// An optional value type is left alone; absence is what optional means.
    /// </summary>
    [Fact]
    public void AnOptionalIntegerIsNotMarked() {
        Assert.DoesNotContain(
            "Required(JsonMetadataServices.CreatePropertyInfo",
            Emit(new PropertyModel { Name = "stock", Type = "integer" }));
    }

    /// <summary>
    /// A declared <c>default</c> does not exempt a required member.
    /// </summary>
    /// <remarks>
    /// <c>required</c> and <c>default</c> are contradictory - one says the caller must send the
    /// member, the other names what absence means - and the contract's <c>required</c> wins. Nor is
    /// it a near miss: a required member's generated parameter carries no <c>= default</c>, so the
    /// specification's default never reaches the constructor. Exempting these would keep the silent
    /// zero for the one shape where the document most obviously disagrees with itself.
    /// </remarks>
    [Fact]
    public void ADeclaredDefaultDoesNotExemptARequiredMember() {
        Assert.Contains(
            "Required(JsonMetadataServices.CreatePropertyInfo",
            Emit(new PropertyModel {
                Name = "stock", Type = "integer", IsRequired = true, Default = "0"
            }));
    }

    /// <summary>
    /// A <c>readOnly</c> member is left alone. <c>required</c> on one means "always present in a
    /// response", and validation runs on request binding - demanding it would reject the create
    /// call of a client that correctly omitted a value the server assigns.
    /// </summary>
    [Fact]
    public void ARequiredReadOnlyMemberIsNotMarked() {
        Assert.DoesNotContain(
            "Required(JsonMetadataServices.CreatePropertyInfo",
            Emit(new PropertyModel {
                Name = "id", Type = "integer", IsRequired = true, IsReadOnly = true
            }));
    }

    /// <summary>
    /// The helper is emitted only where something uses it.
    /// </summary>
    [Fact]
    public void TheHelperIsNotEmittedWhenNothingNeedsIt() {
        Assert.DoesNotContain(
            "property.IsRequired = true",
            Emit(new PropertyModel { Name = "sku", Type = "string" }));
    }

    // ------------------------------------------------- the reflection deserializer's half

    /// <summary>
    /// The model carries <c>[JsonRequired]</c> as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves are needed because the two deserializers read different things.
    /// <c>SystemTextJsonRequestDeserializer</c> is reflection-based and is what an application gets
    /// unless it imports <c>AotSerializerModule</c>; it reads this attribute.
    /// <c>AotRequestDeserializer</c> reads the resolver, which builds every
    /// <c>JsonPropertyInfo</c> by hand and never looks at an attribute.
    /// </para>
    /// <para>
    /// Marking only the resolver passed every unit test and changed nothing about a real request:
    /// the integration application does not import <c>AotSerializerModule</c>, so the metadata that
    /// carried <c>IsRequired</c> was never the metadata in use. <c>readOnly</c> is enforced twice
    /// for the same reason - a null <c>Setter</c> in the resolver, <c>[ResponseOnly]</c> here.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARequiredValueMemberAlsoCarriesJsonRequiredOnTheModel() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "Product",
            Kind = SchemaKind.Object,
            Required = ["unitPriceCents"],
            Properties = [
                new PropertyModel { Name = "unitPriceCents", Type = "integer", IsRequired = true }
            ]
        });

        Assert.Contains("[property: JsonRequired]", result);
    }

    /// <summary>
    /// A required reference type carries both: <c>[JsonRequired]</c> for "was it sent" and
    /// <c>[Required]</c> for "may it be null".
    /// </summary>
    [Fact]
    public void ARequiredReferenceMemberCarriesBoth() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "Product",
            Kind = SchemaKind.Object,
            Required = ["sku"],
            Properties = [new PropertyModel { Name = "sku", Type = "string", IsRequired = true }]
        });

        Assert.Contains("[property: Required]", result);
        Assert.Contains("[property: JsonRequired]", result);
    }

    /// <summary>
    /// A <c>readOnly</c> member carries neither, whatever its type: the server owns the value, and
    /// demanding it would refuse the create call of a client that correctly left it out.
    /// </summary>
    [Fact]
    public void ARequiredReadOnlyReferenceMemberCarriesNeither() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "Product",
            Kind = SchemaKind.Object,
            Required = ["id"],
            Properties = [
                new PropertyModel { Name = "id", Type = "string", IsRequired = true, IsReadOnly = true }
            ]
        });

        Assert.DoesNotContain("JsonRequired", result);
        Assert.DoesNotContain("[property: Required]", result);
    }
}
