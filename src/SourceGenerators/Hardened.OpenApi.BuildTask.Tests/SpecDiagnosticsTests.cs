using System.Linq;
using Hardened.Generation.Models;
using Hardened.Idl;
using Hardened.Generation;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Spec shapes that generate C# which will not compile, caught against the document rather than
/// left to surface as an error in a generated file the author cannot open.
/// </summary>
public class SpecDiagnosticsTests {

    private static ServiceSpecModel SpecWith(string schemaName, params string[] propertyNames) {
        var schema = new SchemaModel { Name = schemaName, Kind = SchemaKind.Object };

        foreach (var name in propertyNames) {
            schema.Properties.Add(new PropertyModel { Name = name, Type = "string" });
        }

        return new ServiceSpecModel { FileName = "spec", Schemas = { schema } };
    }

    /// <summary>
    /// <c>record Message(string Message)</c> is CS0542. <c>{"message": "..."}</c> under a schema
    /// called Message is an ordinary API shape - it is what TechEmpower's json test specifies.
    /// </summary>
    [Fact]
    public void APropertyNamedAfterItsSchemaIsReported() {
        var problem = Assert.Single(SpecDiagnostics.Find(SpecWith("Message", "message")));

        Assert.Equal("HOAT003", problem.Code);
        Assert.Contains("Message", problem.Message);
        Assert.Contains("message", problem.Message);
        Assert.Contains("CS0542", problem.Message);
    }

    /// <summary>The collision is on the generated name, so casing does not avoid it.</summary>
    [Theory]
    [InlineData("Message", "Message")]
    [InlineData("user", "User")]
    [InlineData("Order", "order")]
    public void TheCollisionIsOnThePascalCasedName(string schemaName, string propertyName) {
        Assert.Single(SpecDiagnostics.Find(SpecWith(schemaName, propertyName)));
    }

    /// <summary>Snake case collides too, once both sides are PascalCased.</summary>
    [Fact]
    public void ASnakeCasePropertyCanCollide() {
        Assert.Single(SpecDiagnostics.Find(SpecWith("RandomNumber", "random_number")));
    }

    [Fact]
    public void AnOrdinaryScheamIsNotReported() {
        Assert.Empty(SpecDiagnostics.Find(SpecWith("Message", "text", "id")));
    }

    [Fact]
    public void EachCollidingPropertyIsReportedOnce() {
        Assert.Single(SpecDiagnostics.Find(SpecWith("Thing", "thing", "other")));
    }

    /// <summary>
    /// A name given to a schema written inline colliding with one the document declares.
    /// </summary>
    /// <remarks>
    /// Reachable since inline objects are lifted: a <c>Pet</c> with an inline <c>address</c>
    /// synthesizes <c>PetAddress</c>, which a document is free to have declared already. Renaming
    /// one silently would give the author a public type they did not write.
    /// </remarks>
    [Fact]
    public void TwoSchemasGeneratingOneTypeAreReported() {
        var model = new ServiceSpecModel {
            Schemas = {
                new SchemaModel { Name = "PetAddress", Kind = SchemaKind.Object },
                new SchemaModel { Name = "petAddress", Kind = SchemaKind.Object },
            }
        };

        var problem = Assert.Single(SpecDiagnostics.Find(model));

        Assert.Equal("HOAT005", problem.Code);
        Assert.Contains("PetAddress", problem.Message);
    }

    /// <summary>Distinct names are not reported, however similar.</summary>
    [Fact]
    public void DistinctSchemaNamesAreClean() {
        var model = new ServiceSpecModel {
            Schemas = {
                new SchemaModel { Name = "Pet", Kind = SchemaKind.Object },
                new SchemaModel { Name = "PetAddress", Kind = SchemaKind.Object },
            }
        };

        Assert.Empty(SpecDiagnostics.Find(model));
    }

}
