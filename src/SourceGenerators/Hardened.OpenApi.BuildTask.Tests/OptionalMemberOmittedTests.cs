using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// A member the description declares optional and not nullable is absent from a response rather
/// than written as <c>null</c>.
/// </summary>
/// <remarks>
/// Both serializers, because they read different things: the reflection-based one reads the
/// attribute the record carries, the source-generated resolver reads the <c>IgnoreCondition</c> on
/// the property info it builds by hand. They answered the same contract differently, and the one a
/// deployed application registers decided whether its own document was honoured.
/// </remarks>
public class OptionalMemberOmittedTests {

    private static SchemaModel Subscription() =>
        new() {
            Name = "Subscription",
            Kind = SchemaKind.Object,
            Required = new List<string> { "status" },
            Properties = new List<PropertyModel> {
                // Required and not nullable: always present, never null.
                new() { Name = "status", Type = "string", IsRequired = true },

                // Optional and not nullable: absent, or an integer. Null is neither.
                new() { Name = "expiresAt", Type = "integer", Format = "int64" },

                // Required and nullable: always present, and null is one of its values.
                new() { Name = "productId", Type = "string", IsRequired = true, IsNullable = true },

                // Optional and nullable: the document permits both, so nothing changes.
                new() { Name = "inTrial", Type = "boolean", IsNullable = true }
            }
        };

    [Fact]
    public void TheRecordCarriesTheIgnoreOnOptionalNonNullableMembersOnly() {
        var result = EmitterHarness.Schema(Subscription());

        const string ignore =
            "[property: JsonIgnore(Condition = global::System.Text.Json.Serialization." +
            "JsonIgnoreCondition.WhenWritingNull)]";

        Assert.Contains(ignore + " long? ExpiresAt", result);

        Assert.DoesNotContain(ignore + " string Status", result);
        Assert.DoesNotContain(ignore + " string? ProductId", result);
        Assert.DoesNotContain(ignore + " bool? InTrial", result);
    }

    [Fact]
    public void TheResolverCarriesTheSameConditionOnTheSameMembers() {
        var result = EmitterHarness.JsonTypeInfo(new List<SchemaModel> { Subscription() }, "petstore");

        var expiresAt = Section(result, "expiresAt");

        Assert.Contains("IgnoreCondition = JsonIgnoreCondition.WhenWritingNull", expiresAt);

        Assert.DoesNotContain("IgnoreCondition", Section(result, "status"));
        Assert.DoesNotContain("IgnoreCondition", Section(result, "productId"));
        Assert.DoesNotContain("IgnoreCondition", Section(result, "inTrial"));
    }

    /// <summary>One property's entry, from its wire name to the end of the object literal.</summary>
    private static string Section(string source, string wireName) {
        var start = source.IndexOf("PropertyName = \"" + wireName + "\"", System.StringComparison.Ordinal);

        Assert.True(start >= 0, wireName + " has no property info");

        var end = source.IndexOf("})", start, System.StringComparison.Ordinal);

        return source.Substring(start, end - start);
    }
}
