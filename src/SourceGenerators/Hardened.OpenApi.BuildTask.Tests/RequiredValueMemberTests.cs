using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Catching a required member the C# type cannot prove was sent.
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
/// <c>[Required]</c> cannot cover it: the validation generator emits <c>value.x is null</c>, which
/// is CS0037 against an <c>int</c>, so the constraint is correctly suppressed and nothing replaced
/// it. The deserializer is the only layer that still knows the member was absent.
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

        Assert.Contains("Setter = static (object obj, int value) => { },", result);
    }

    /// <summary>
    /// A required reference type is left alone. It already carries <c>[Required]</c>, and the
    /// generated validator aggregates its error with every other failed constraint in the same body
    /// - which the deserializer, stopping at the first fault, would not.
    /// </summary>
    [Fact]
    public void ARequiredStringIsLeftToTheValidator() {
        var result = Emit(new PropertyModel { Name = "sku", Type = "string", IsRequired = true });

        Assert.DoesNotContain("Required(JsonMetadataServices.CreatePropertyInfo", result);
        Assert.DoesNotContain("property.IsRequired = true", result);
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
            Emit(new PropertyModel { Name = "sku", Type = "string", IsRequired = true }));
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
    /// A required reference type does not, because <c>[Required]</c> already covers it and the
    /// validator aggregates where the deserializer stops at the first fault.
    /// </summary>
    [Fact]
    public void ARequiredReferenceMemberDoesNotCarryJsonRequired() {
        var result = EmitterHarness.Schema(new SchemaModel {
            Name = "Product",
            Kind = SchemaKind.Object,
            Required = ["sku"],
            Properties = [new PropertyModel { Name = "sku", Type = "string", IsRequired = true }]
        });

        Assert.Contains("[property: Required]", result);
        Assert.DoesNotContain("JsonRequired", result);
    }
}
