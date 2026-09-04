using System.Collections.Generic;
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

    #region a path token nothing declares

    private static ServiceSpecModel SpecWithRoute(string path, params string[] pathParameters) {
        var operation = new OperationModel {
            OperationId = "getNote",
            HttpMethod = "GET",
            Path = path
        };

        foreach (var name in pathParameters) {
            operation.Parameters.Add(new ParameterModel { Name = name, In = "path" });
        }

        return new ServiceSpecModel {
            FileName = "spec",
            Services = { new ServiceModel { Tag = "Pet", Operations = { operation } } }
        };
    }

    private static IEnumerable<SpecDiagnostics.Problem> Unbound(ServiceSpecModel model) =>
        SpecDiagnostics.Find(model, "HOAT").Where(problem => problem.Code == "HOAT026");

    /// <summary>
    /// OpenAPI requires a template expression in a path to have a matching path parameter, and a
    /// description that breaks the rule built clean. The route table registers the token so the
    /// route still matches, the service interface omits it so the handler cannot read it, and the
    /// generated link method takes it - three things disagreeing about one segment.
    /// </summary>
    [Fact]
    public void APathTokenNoParameterDeclaresIsReported() {
        var problem = Assert.Single(
            Unbound(SpecWithRoute("/pets/{petId}/notes/{noteId}", "petId")));

        Assert.Equal("HOAT026", problem.Code);
        Assert.Contains("noteId", problem.Message);
        Assert.Contains("GET /pets/{petId}/notes/{noteId}", problem.Message);
    }

    /// <summary>
    /// A warning. The generator has an answer - match the token and discard the value - and a
    /// description fetched from elsewhere is not always the author's to correct.
    /// </summary>
    [Fact]
    public void TheReportIsNotFatal() {
        Assert.False(Assert.Single(Unbound(SpecWithRoute("/pets/{petId}"))).Fatal);
    }

    [Fact]
    public void EveryDeclaredTokenReportsNothing() {
        Assert.Empty(Unbound(SpecWithRoute("/pets/{petId}/notes/{noteId}", "petId", "noteId")));
    }

    [Fact]
    public void APathWithNoTokensReportsNothing() {
        Assert.Empty(Unbound(SpecWithRoute("/pets")));
    }

    [Fact]
    public void EachUndeclaredTokenIsItsOwnReport() {
        Assert.Equal(2, Unbound(SpecWithRoute("/pets/{petId}/notes/{noteId}")).Count());
    }

    /// <summary>
    /// Path parameters match by exact name, so a case-only difference declares nothing - and it is
    /// never what anyone meant, which is why the message says so.
    /// </summary>
    [Fact]
    public void ANameDifferingOnlyInCaseIsReportedAndNamed() {
        var problem = Assert.Single(Unbound(SpecWithRoute("/pets/{petid}", "petId")));

        Assert.Contains("petid", problem.Message);
        Assert.Contains("only in case", problem.Message);
    }

    /// <summary>A query parameter of the same name does not bind a path token.</summary>
    [Fact]
    public void AParameterInAnotherLocationDoesNotDeclareTheToken() {
        var model = SpecWithRoute("/pets/{petId}");

        model.Services[0].Operations[0].Parameters.Add(
            new ParameterModel { Name = "petId", In = "query" });

        Assert.Single(Unbound(model));
    }

    /// <summary>
    /// The route-constraint form names its parameter before the colon, so the token is the part
    /// that has to be declared.
    /// </summary>
    [Fact]
    public void AConstrainedTokenIsMatchedOnItsNameAlone() {
        Assert.Empty(Unbound(SpecWithRoute("/pets/{petId:int}", "petId")));
    }

    #endregion

    /// <summary>
    /// <c>record Message(string Message)</c> is CS0542. <c>{"message": "..."}</c> under a schema
    /// called Message is an ordinary API shape - it is what TechEmpower's json test specifies.
    /// </summary>
    [Fact]
    public void APropertyNamedAfterItsSchemaIsReported() {
        var problem = Assert.Single(SpecDiagnostics.Find(SpecWith("Message", "message"), "HOAT"));

        Assert.Equal("HOAT020", problem.Code);
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
        Assert.Single(SpecDiagnostics.Find(SpecWith(schemaName, propertyName), "HOAT"));
    }

    /// <summary>Snake case collides too, once both sides are PascalCased.</summary>
    [Fact]
    public void ASnakeCasePropertyCanCollide() {
        Assert.Single(SpecDiagnostics.Find(SpecWith("RandomNumber", "random_number"), "HOAT"));
    }

    [Fact]
    public void AnOrdinaryScheamIsNotReported() {
        Assert.Empty(SpecDiagnostics.Find(SpecWith("Message", "text", "id"), "HOAT"));
    }

    [Fact]
    public void EachCollidingPropertyIsReportedOnce() {
        Assert.Single(SpecDiagnostics.Find(SpecWith("Thing", "thing", "other"), "HOAT"));
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

        var problem = Assert.Single(SpecDiagnostics.Find(model, "HOAT"));

        Assert.Equal("HOAT021", problem.Code);
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

        Assert.Empty(SpecDiagnostics.Find(model, "HOAT"));
    }

}
