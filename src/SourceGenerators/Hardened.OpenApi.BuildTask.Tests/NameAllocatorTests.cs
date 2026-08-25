using System.Collections.Generic;
using System.Threading;
using Hardened.Idl;
using Hardened.Generation;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The properties the allocator has to hold, rather than the names it happens to produce today.
/// </summary>
/// <remarks>
/// Ten collisions were fixed one at a time before it was clear they were one missing component. The
/// cases below are those ten, but the two tests that matter most are the invariants: allocated
/// names survive being sanitized again, and they do not depend on the order a document lists things
/// in. Those are what stop the next document finding an eleventh.
/// </remarks>
public class NameAllocatorTests {

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "spec", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    private static IEnumerable<string> AllNames(ServiceSpecModel model) {
        foreach (var schema in model.Schemas) {
            yield return schema.Name;

            foreach (var property in schema.Properties) {
                yield return property.MemberName;
            }

            foreach (var member in schema.EnumMembers) {
                yield return member;
            }
        }

        foreach (var service in model.Services) {
            yield return service.TypeBaseName;

            foreach (var operation in service.Operations) {
                yield return operation.MethodName;
            }
        }
    }

    /// <summary>
    /// The invariant the emitters depend on.
    /// </summary>
    /// <remarks>
    /// Some forty call sites re-derive a name by sanitizing it. That is only safe while sanitizing
    /// an allocated name returns it unchanged - otherwise a call site that re-derives disagrees
    /// with the allocator, which is how two names that were made distinct converge again. It is
    /// also why a disambiguating suffix carries no <c>_</c>.
    /// </remarks>
    [Fact]
    public void EveryAllocatedNameSurvivesBeingSanitizedAgain() {
        var model = Parse(Colliding);

        foreach (var name in AllNames(model)) {
            Assert.Equal(name, NamingHelper.ToPascalCase(name));
        }
    }

    /// <summary>
    /// Reordering a document must not rename anything.
    /// </summary>
    /// <remarks>
    /// Disambiguation is derived from what a thing is rather than from how many names came before
    /// it, so the same document always produces the same names. Without this a consumer's handler
    /// breaks when a vendor reorders their file.
    /// </remarks>
    [Fact]
    public void NamesDoNotDependOnTheOrderTheDocumentListsThings() {
        var forward = new List<string>(AllNames(Parse(Colliding)));
        var reversed = new List<string>(AllNames(Parse(CollidingReordered)));

        forward.Sort(System.StringComparer.Ordinal);
        reversed.Sort(System.StringComparer.Ordinal);

        Assert.Equal(forward, reversed);
    }

    /// <summary>
    /// Renaming, not dropping.
    /// </summary>
    /// <remarks>
    /// Every test below asserts that some name is <em>not</em> present, and a schema the parser
    /// silently discarded would satisfy all of them. This is the one that says the document still
    /// produces everything it declares - eight schemas, and the six that had to move are still
    /// reachable under the name they were given.
    /// </remarks>
    [Fact]
    public void CollidingDeclarationsAreRenamedRatherThanDropped() {
        var model = Parse(Colliding);

        Assert.Equal(8, model.Schemas.Count);

        foreach (var declared in new[] { "Monitor", "NullTime", "Commit", "Column" }) {
            Assert.Contains(model.Schemas, schema => schema.Name == declared);
        }

        // The ones that had to move carry the document's name and then their own, so a reader can
        // still tell which declaration a renamed type came from - see SpecNameQualifiesATypeThatHadToMove.
        foreach (var moved in new[] { "MonitorValidator", "DateTime", "JsonElement" }) {
            Assert.Contains(model.Schemas, schema => schema.Name == "Spec" + moved);
        }
    }

    /// <summary>
    /// What a moved name is called, rather than merely that it moved.
    /// </summary>
    /// <remarks>
    /// The first version of this hashed the same information and produced
    /// <c>DateTimeN9bec7490</c>. It was stable and it was unreadable, and stability was never the
    /// part that was hard - so a name that has to move is qualified by the scope that owns it,
    /// which is the thing that distinguishes it from whatever it collided with.
    /// </remarks>
    [Fact]
    public void ANameThatHadToMoveIsQualifiedByTheScopeThatOwnsIt() {
        var model = Parse(Colliding);

        // A schema is qualified by the document. Zoom's DateTime becomes ZoomDateTime; here the
        // document is called "spec".
        Assert.Contains(model.Schemas, schema => schema.Name == "SpecDateTime");

        var commit = Assert.Single(model.Schemas, schema => schema.Name == "Commit");

        // A property is qualified by the type that declares it. Bitbucket's repository.clone
        // becomes RepositoryClone; Jira's toString and GitHub's self-named property likewise.
        var members = commit.Properties.ConvertAll(property => property.MemberName);

        Assert.Contains("CommitClone", members);
        Assert.Contains("CommitToString", members);
        Assert.Contains("CommitCommit", members);

        // An enum member is qualified by its enum.
        var column = Assert.Single(model.Schemas, schema => schema.Name == "Column");

        Assert.Contains("ColumnBucketsCount", column.EnumMembers);

        // A parameter is qualified by where it travels - Kubernetes' two called path.
        var operation = Assert.Single(
            model.Services[0].Operations, candidate => candidate.OperationId == "getThing");

        Assert.Contains("queryPath", operation.Parameters.ConvertAll(p => p.MemberName));

        // An operation is the one exception: two ids that collide share a tag, so the tag
        // distinguishes nothing and the route does. This is the name it would have had with no id.
        Assert.Contains(model.Services[0].Operations,
            candidate => candidate.MethodName == "DeleteThingsByPath");
    }

    /// <summary>Nothing carries a hash, which is what this replaced.</summary>
    [Fact]
    public void NoAllocatedNameIsAHash() {
        var model = Parse(Colliding);

        foreach (var name in AllNames(model)) {
            Assert.DoesNotMatch("N[0-9a-f]{8}$", name);
        }
    }

    [Fact]
    public void EveryNameIsUniqueWithinItsScope() {
        var model = Parse(Colliding);

        var types = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var schema in model.Schemas) {
            Assert.True(types.Add(schema.Name), $"duplicate type {schema.Name}");

            var members = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var property in schema.Properties) {
                Assert.True(members.Add(property.MemberName),
                    $"duplicate member {schema.Name}.{property.MemberName}");
            }

            var values = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var member in schema.EnumMembers) {
                Assert.True(values.Add(member), $"duplicate enum member {schema.Name}.{member}");
            }
        }

        var methods = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                Assert.True(methods.Add(operation.MethodName),
                    $"duplicate method {operation.MethodName}");
            }
        }
    }

    /// <summary>A property may not be named after the type declaring it - CS0542.</summary>
    [Fact]
    public void APropertyIsNeverNamedAfterItsOwnType() {
        var model = Parse(Colliding);

        foreach (var schema in model.Schemas) {
            foreach (var property in schema.Properties) {
                Assert.NotEqual(schema.Name, property.MemberName);
            }
        }
    }

    /// <summary>A property may not be named after a member every type already has - CS0102.</summary>
    [Fact]
    public void APropertyIsNeverNamedAfterAMemberOfObject() {
        var model = Parse(Colliding);

        foreach (var schema in model.Schemas) {
            foreach (var property in schema.Properties) {
                Assert.DoesNotContain(property.MemberName,
                    new[] { "ToString", "Equals", "GetHashCode", "GetType" });
            }
        }
    }

    /// <summary>
    /// The validation generator names a validator after the type it checks, so a schema cannot be
    /// called that - see Sentry's Monitor beside MonitorValidator.
    /// </summary>
    [Fact]
    public void NoTypeIsNamedAfterAnotherTypesGeneratedValidator() {
        var model = Parse(Colliding);

        var types = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var schema in model.Schemas) {
            types.Add(schema.Name);
        }

        foreach (var name in types) {
            if (name.EndsWith("Validator", System.StringComparison.Ordinal)) {
                Assert.DoesNotContain(
                    name.Substring(0, name.Length - "Validator".Length), types);
            }
        }
    }

    /// <summary>The document's own values are not the allocator's to change.</summary>
    [Fact]
    public void TheDocumentsOwnIdentifiersAreLeftAlone() {
        var model = Parse(Colliding);

        var ids = new List<string>();

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                ids.Add(operation.OperationId);
            }
        }

        Assert.Contains("deleteThing", ids);
        Assert.Contains("DeleteThing", ids);
    }

    /// <summary>
    /// Every collision the published corpus produced, in one document.
    /// </summary>
    /// <remarks>
    /// <c>Monitor</c>/<c>MonitorValidator</c> is Sentry, <c>NullTime</c>/<c>nullTime</c> is Ory,
    /// <c>deleteThing</c>/<c>DeleteThing</c> is Cloudflare, <c>toString</c> is Jira, the property
    /// named after its type is GitHub, the repeated enum values are Elasticsearch, the empty value
    /// is Docker and Cloudflare, and the two parameters called <c>path</c> are Kubernetes.
    /// </remarks>
    private const string Colliding = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /things/{path}:
            get:
              tags: [Thing]
              operationId: getThing
              parameters:
                - { name: path, in: path, required: true, schema: { type: string } }
                - { name: path, in: query, schema: { type: string } }
              responses:
                '200': { description: ok }
            delete:
              tags: [Thing]
              operationId: deleteThing
              responses:
                '200': { description: ok }
          /things:
            delete:
              tags: [Thing]
              operationId: DeleteThing
              responses:
                '200': { description: ok }
        components:
          schemas:
            Monitor:
              type: object
              properties: { name: { type: string } }
            MonitorValidator:
              type: object
              properties: { name: { type: string } }
            NullTime:
              type: object
              properties: { at: { type: string } }
            nullTime:
              type: object
              properties: { at: { type: string } }
            Commit:
              type: object
              properties:
                commit: { type: string }
                toString: { type: string }
                clone: { type: string }
                name: { type: string }
                Name: { type: string }
            DateTime:
              type: object
              properties:
                from: { type: string }
                to: { type: string }
            JsonElement:
              type: object
              properties: { raw: { type: string } }
            Column:
              type: string
              enum: ["buckets.count", "buckets_count", "", "bc"]
        """;

    /// <summary>
    /// <c>Clone</c> is not merely taken, it is forbidden: a record may not declare a member of that
    /// name at all (CS8859). Bitbucket declares a property called <c>clone</c>.
    /// </summary>
    [Fact]
    public void NoPropertyIsNamedAfterAMemberARecordReserves() {
        var model = Parse(Colliding);

        foreach (var schema in model.Schemas) {
            foreach (var property in schema.Properties) {
                Assert.NotEqual("Clone", property.MemberName);
                Assert.NotEqual("Deconstruct", property.MemberName);
                Assert.NotEqual("PrintMembers", property.MemberName);
                Assert.NotEqual("EqualityContract", property.MemberName);
            }
        }
    }

    /// <summary>
    /// A schema may not keep a name the type mapper resolves by spelling.
    /// </summary>
    /// <remarks>
    /// The mapper answers to the rendered name, so a schema called <c>DateTime</c> became
    /// <c>System.DateTime</c> everywhere it was referenced and the record it generated was
    /// unreachable. Zoom declares one - and it is a date <em>range</em>, with <c>from</c> and
    /// <c>to</c>, so binding it to the BCL type would have been wrong as well as unbuildable.
    /// </remarks>
    [Theory]
    [InlineData("DateTime")]
    [InlineData("DateOnly")]
    [InlineData("JsonElement")]
    public void NoSchemaKeepsANameThePrimitiveMapperAnswersTo(string reserved) {
        var model = Parse(Colliding);

        foreach (var schema in model.Schemas) {
            Assert.NotEqual(reserved, schema.Name);
        }
    }

    /// <summary>The same document with everything listed the other way round.</summary>
    private const string CollidingReordered = """
        openapi: "3.0.3"
        info: { title: T, version: "1.0" }
        paths:
          /things:
            delete:
              tags: [Thing]
              operationId: DeleteThing
              responses:
                '200': { description: ok }
          /things/{path}:
            delete:
              tags: [Thing]
              operationId: deleteThing
              responses:
                '200': { description: ok }
            get:
              tags: [Thing]
              operationId: getThing
              parameters:
                - { name: path, in: query, schema: { type: string } }
                - { name: path, in: path, required: true, schema: { type: string } }
              responses:
                '200': { description: ok }
        components:
          schemas:
            Column:
              type: string
              enum: ["buckets.count", "buckets_count", "", "bc"]
            JsonElement:
              type: object
              properties: { raw: { type: string } }
            DateTime:
              type: object
              properties:
                to: { type: string }
                from: { type: string }
            Commit:
              type: object
              properties:
                Name: { type: string }
                name: { type: string }
                clone: { type: string }
                toString: { type: string }
                commit: { type: string }
            nullTime:
              type: object
              properties: { at: { type: string } }
            NullTime:
              type: object
              properties: { at: { type: string } }
            MonitorValidator:
              type: object
              properties: { name: { type: string } }
            Monitor:
              type: object
              properties: { name: { type: string } }
        """;
}
