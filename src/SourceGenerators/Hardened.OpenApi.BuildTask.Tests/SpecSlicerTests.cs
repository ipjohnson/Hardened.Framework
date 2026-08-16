using System.Collections.Generic;
using System.Threading;
using Hardened.OpenApi.BuildTask.Filtering;
using Hardened.OpenApi.SourceGenerator;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Narrowing a description to the part one service implements.
/// </summary>
public class SpecSlicerTests {

    /// <summary>
    /// Two operations under different paths, each reaching a schema of its own, plus a schema
    /// neither reaches.
    /// </summary>
    private const string TwoServices = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /repos/{owner}/releases:
            get:
              tags: [Releases]
              operationId: listReleases
              parameters:
                - name: owner
                  in: path
                  required: true
                  schema: { type: string }
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Release' }
          /issues:
            get:
              tags: [Issues]
              operationId: listIssues
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Issue' }
        components:
          schemas:
            Release:
              type: object
              required: [id]
              properties:
                id: { type: string }
                author: { $ref: '#/components/schemas/User' }
            User:
              type: object
              properties:
                login: { type: string }
            Issue:
              type: object
              properties:
                title: { type: string }
            Unreferenced:
              type: object
              properties:
                nothing: { type: string }
        """;

    private static ServiceSpecModel Parse() {
        var model = OpenApiSpecParser.Parse(TwoServices, "spec", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static SpecSlicer.Filter Paths(params string[] globs) =>
        new() { IncludePaths = globs };

    [Fact]
    public void AnEmptyFilterKeepsEveryOperation() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, new SpecSlicer.Filter());

        Assert.Equal(2, result.OperationsKept);
        Assert.Equal(0, result.OperationsDropped);
        Assert.False(result.MatchedNothing);
    }

    /// <summary>
    /// A schema nothing reaches is not generated, filter or no filter.
    /// </summary>
    /// <remarks>
    /// A description declares component schemas; it does not promise an operation uses them. Zoom
    /// declares a <c>DateTime</c> object that nothing in the document references, and generating it
    /// produced a type that had to be renamed away from the BCL name in order to compile - a name
    /// collision, and a type, over something no caller could reach.
    /// </remarks>
    [Fact]
    public void AnUnreferencedSchemaIsDroppedEvenWithNoFilter() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, new SpecSlicer.Filter());

        Assert.Equal(1, result.SchemasDropped);
        Assert.DoesNotContain(model.Schemas, schema => schema.Name == "Unreferenced");

        // Everything an operation reaches is still there.
        Assert.Equal(3, model.Schemas.Count);
    }

    /// <summary>
    /// A subtype of a discriminated base survives, though nothing names it.
    /// </summary>
    /// <remarks>
    /// Every other edge in the closure runs from a use to what it names, and this is the one thing
    /// nothing names: an operation returns <c>Pet</c>, the wire carries a <c>Dog</c>, and the only
    /// trace of <c>Dog</c> is its own <c>allOf</c> pointing back. Dropping it would compile and
    /// then fail to deserialize the response it was told to expect.
    /// </remarks>
    [Fact]
    public void ASubtypeOfADiscriminatedBaseIsReachedThroughTheBase() {
        var model = OpenApiSpecParser.Parse(Polymorphic, "spec", CancellationToken.None)!;

        SpecSlicer.Apply(model, new SpecSlicer.Filter());

        var names = model.Schemas.ConvertAll(schema => schema.Name);

        Assert.Contains("Pet", names);
        Assert.Contains("Dog", names);
        Assert.Contains("Cat", names);

        // Reuse is not substitution: nothing says an Address arrives where a Contact was asked for.
        Assert.DoesNotContain("Employee", names);
    }

    /// <summary>
    /// A base with a discriminator whose subtypes point back at it with <c>allOf</c>, beside a plain
    /// <c>allOf</c> that is only reuse.
    /// </summary>
    private const string Polymorphic = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /pets:
            get:
              tags: [Pet]
              operationId: getPet
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema: { $ref: '#/components/schemas/Pet' }
        components:
          schemas:
            Pet:
              type: object
              required: [petType]
              discriminator: { propertyName: petType }
              properties:
                petType: { type: string }
            Dog:
              allOf:
                - $ref: '#/components/schemas/Pet'
                - type: object
                  properties: { bark: { type: string } }
            Cat:
              allOf:
                - $ref: '#/components/schemas/Pet'
                - type: object
                  properties: { meow: { type: string } }
            Contact:
              type: object
              properties: { email: { type: string } }
            Employee:
              allOf:
                - $ref: '#/components/schemas/Contact'
                - type: object
                  properties: { badge: { type: string } }
        """;

    /// <summary>The escape hatch, for a project that uses a declared type the document never does.</summary>
    [Fact]
    public void KeepingUnreferencedSchemasIsAvailable() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, new SpecSlicer.Filter(), keepUnreferenced: true);

        Assert.Equal(0, result.SchemasDropped);
        Assert.Equal(4, model.Schemas.Count);
        Assert.Contains(model.Schemas, schema => schema.Name == "Unreferenced");
    }

    [Fact]
    public void APathGlobKeepsOnlyTheOperationsUnderIt() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, Paths("/repos/**"));

        Assert.Equal(1, result.OperationsKept);
        Assert.Equal(1, result.OperationsDropped);
        Assert.Single(model.Services);
        Assert.Equal("listReleases", model.Services[0].Operations[0].OperationId);
    }

    /// <summary>
    /// The closure, which is where the size actually is: the kept operation reaches
    /// <c>Release</c>, and <c>Release</c> reaches <c>User</c>.
    /// </summary>
    [Fact]
    public void SchemasAreKeptTransitivelyAndNothingElseSurvives() {
        var model = Parse();

        SpecSlicer.Apply(model, Paths("/repos/**"));

        var names = new List<string>();

        foreach (var schema in model.Schemas) {
            names.Add(schema.Name);
        }

        Assert.Contains("Release", names);
        Assert.Contains("User", names);
        Assert.DoesNotContain("Issue", names);
        Assert.DoesNotContain("Unreferenced", names);
    }

    [Fact]
    public void NothingKeptStillPointsAtSomethingRemoved() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, Paths("/repos/**"));

        Assert.Empty(result.DanglingReferences);
    }

    [Fact]
    public void AnExcludeIsAppliedAfterTheIncludeSet() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, new SpecSlicer.Filter {
            IncludePaths = new[] { "/**" },
            ExcludePaths = new[] { "/repos/**" }
        });

        Assert.Equal(1, result.OperationsKept);
        Assert.Equal("listIssues", model.Services[0].Operations[0].OperationId);
    }

    [Fact]
    public void TagsSelectTheSameWayPathsDo() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, new SpecSlicer.Filter { Tags = new[] { "Issues" } });

        Assert.Equal(1, result.OperationsKept);
        Assert.Equal("listIssues", model.Services[0].Operations[0].OperationId);
    }

    /// <summary>
    /// A filter that selects nothing is the failure worth catching: the build would otherwise
    /// succeed against an empty project.
    /// </summary>
    [Fact]
    public void AFilterMatchingNothingSaysSo() {
        var model = Parse();

        var result = SpecSlicer.Apply(model, Paths("/nothing/here/**"));

        Assert.True(result.MatchedNothing);
        Assert.Equal(0, result.OperationsKept);
    }

    [Theory]
    // ** spans any number of segments, including none.
    [InlineData("/repos/**", "/repos/a/b/c", true)]
    [InlineData("/repos/**", "/repos", true)]
    [InlineData("/repos/**", "/issues/a", false)]
    // * is one segment, and a path parameter is ordinary text to it.
    [InlineData("/repos/*/issues", "/repos/{owner}/issues", true)]
    [InlineData("/repos/*/issues", "/repos/a/b/issues", false)]
    // ** in the middle has to find the rest after it.
    [InlineData("/repos/**/releases", "/repos/a/b/releases", true)]
    [InlineData("/repos/**/releases", "/repos/a/b/tags", false)]
    // A glob with no wildcard is an exact match on the whole path.
    [InlineData("/issues", "/issues", true)]
    [InlineData("/issues", "/issues/1", false)]
    // Partial segment wildcards.
    [InlineData("/v*/things", "/v2/things", true)]
    [InlineData("/v*/things", "/api/things", false)]
    public void GlobsMatchBySegment(string glob, string path, bool expected) {
        Assert.Equal(expected, SpecSlicer.GlobMatches(glob, path));
    }
}
