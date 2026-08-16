using System.Collections.Generic;
using System.Threading;
using Hardened.Idl;
using Hardened.Idl.Models;
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
                name: { type: string }
                Name: { type: string }
            Column:
              type: string
              enum: ["buckets.count", "buckets_count", "", "bc"]
        """;

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
            Commit:
              type: object
              properties:
                Name: { type: string }
                name: { type: string }
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
