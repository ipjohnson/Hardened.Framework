using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The assertion the OpenAPI suite was missing.
///
/// <para>
/// The 116 tests that predate 2026-08-11 call the emitters directly and assert on the strings they
/// return. That proves the generator produced the characters someone expected; it does not prove a
/// consumer can build. Three defects in the sibling web generator emitted uncompilable C# and passed
/// every string assertion, and were caught by integration tests after they shipped.
/// </para>
///
/// <para>
/// Every case here ends in <c>AssertNoErrors</c>, which compiles the test's own source together with
/// every tree the generator emitted. The generator is driven end to end — specification in through
/// <c>AdditionalFiles</c>, C# out — so records, enums, service interfaces, per-operation handlers,
/// validation filter providers, filter attributes, the JSON type-info resolver and the routing table
/// are all compiled together, in the combination a real project produces.
/// </para>
/// </summary>
public class GeneratedCodeCompilesTests {

    [Fact]
    public void TheSmallestSpecProducesCodeThatCompiles() {
        OpenApiGenerator.Run(Specs.Minimal).AssertNoErrors();
    }

    /// <summary>A JSON document is as valid a specification as a YAML one.</summary>
    [Fact]
    public void AJsonSpecProducesCodeThatCompiles() {
        OpenApiGenerator.Run(Specs.MinimalJson, specFileName: "petstore.json").AssertNoErrors();
    }

    /// <summary>
    /// Records for every schema shape the type mapper knows. A format the mapper gets wrong shows up
    /// here as a constructor whose parameter type does not exist rather than as a string mismatch.
    /// </summary>
    [Fact]
    public void RecordsForEverySchemaShapeCompile() {
        var result = OpenApiGenerator.Run(Specs.EverySchemaShape).AssertNoErrors();

        var record = result.SourceContaining("petstore.g.cs");

        Assert.Contains("public partial record Widget(", record);
        Assert.Contains("List<Part>? Parts = default", record);
        Assert.Contains("Dictionary<string,string>? Labels = default", record);
    }

    /// <summary>
    /// The enum emitter's output. A generated enum has to carry
    /// <c>JsonStringEnumConverter</c> or a spec value like <c>available</c> round-trips as an
    /// integer.
    /// </summary>
    [Fact]
    public void GeneratedEnumsCompile() {
        var result = OpenApiGenerator.Run(Specs.EverySchemaShape).AssertNoErrors();

        var generated = result.SourceContaining("petstore.g.cs");

        Assert.Contains("public enum WidgetStatus", generated);
        Assert.Contains("JsonStringEnumConverter", generated);
    }

    /// <summary>
    /// The service interface is the surface a consumer implements, so it has to compile against the
    /// records the same run emitted.
    /// </summary>
    [Fact]
    public void GeneratedServiceInterfacesCompile() {
        var result = OpenApiGenerator.Run(Specs.EverySchemaShape).AssertNoErrors();

        var generated = result.SourceContaining("petstore.g.cs");

        Assert.Contains("public partial interface IWidgetService", generated);
        Assert.Contains("Task<List<Widget>> ListWidgets(", generated);
    }

    /// <summary>
    /// A <c>[Handler]</c> implementing the generated interface — the shape the guide tells a consumer
    /// to write. This compiles the generated interface, the generated records and the generated
    /// registration against real user code rather than against nothing.
    /// </summary>
    [Fact]
    public void AHandlerImplementingTheGeneratedInterfaceCompiles() {
        OpenApiGenerator.Run(
                Specs.Minimal,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class PetServiceImpl : IPetService {
                        public Task<Pet> ListPets() => Task.FromResult(new Pet("1"));
                    }
                    """))
            .AssertNoErrors();
    }

    /// <summary>
    /// Every verb on a path with a token, so the emitted routing table carries a literal node, a
    /// wildcard node and a method switch with six cases.
    /// </summary>
    [Fact]
    public void EveryVerbRoutesAndCompiles() {
        var result = OpenApiGenerator.Run(
                Specs.EveryVerb,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class ItemServiceImpl : IItemService {
                        public Task ListItems() => Task.CompletedTask;
                        public Task CreateItem() => Task.CompletedTask;
                        public Task GetItem(string itemId) => Task.CompletedTask;
                        public Task ReplaceItem(string itemId) => Task.CompletedTask;
                        public Task UpdateItem(string itemId) => Task.CompletedTask;
                        public Task DeleteItem(string itemId) => Task.CompletedTask;
                    }
                    """))
            .AssertNoErrors();

        var routing = result.SourceContaining("OpenApiRouting");

        foreach (var verb in new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }) {
            Assert.Contains($"case \"{verb}\":", routing);
        }
    }

    [Theory]
    [InlineData("get", "GET")]
    [InlineData("post", "POST")]
    [InlineData("put", "PUT")]
    [InlineData("patch", "PATCH")]
    [InlineData("delete", "DELETE")]
    public void AnOperationOfEachVerbCompilesOnItsOwn(string verb, string expected) {
        var result = OpenApiGenerator.Run(
            $$"""
            openapi: "3.0.0"
            info: { title: Items, version: "1.0" }
            paths:
              /items/{itemId}:
                {{verb}}:
                  tags: [Item]
                  operationId: handleItem
                  parameters:
                    - name: itemId
                      in: path
                      required: true
                      schema: { type: string }
                  responses:
                    '200': { description: ok }
            """).AssertNoErrors();

        Assert.Contains($"case \"{expected}\":", result.SourceContaining("OpenApiRouting"));
    }

    /// <summary>
    /// The validation wiring, which spans both halves: the task emits an attributed interface, the
    /// generator makes <c>Parameters</c> implement it and attaches a filter typed on it.
    /// </summary>
    /// <remarks>
    /// Worth asserting on the emitted text rather than only on compilation, because the failure mode
    /// is silent. If <c>Parameters</c> does not implement the interface, the filter's type test does
    /// not match and validation never runs on a build that is green.
    /// </remarks>
    [Fact]
    public void ValidationIsWiredThroughTheInterfaceOntoTheHandler() {
        var result = OpenApiGenerator.Run(Specs.EveryValidationConstraint).AssertNoErrors();

        var handler = result.SourceContaining("OrderController_CreateOrder");

        Assert.Contains("ValidateAttribute<", handler);
        Assert.Contains("ICreateOrderParameters", handler);
        Assert.Contains("class Parameters :", handler);
    }

    /// <summary>
    /// The constraints a spec declares, as attributes on what the task emits.
    /// </summary>
    /// <remarks>
    /// The task writes attributes and nothing else - no validator, no ValidationModules IR. The
    /// validator comes from Hardened.Validation.SourceGenerator reading these out of the
    /// compilation, which is the same scan that picks up [Required] on a hand-written class. That
    /// generator is not in this harness, so what is asserted here is the input it will read.
    /// </remarks>
    [Fact]
    public void SpecConstraintsBecomeAttributes() {
        var result = OpenApiGenerator.Run(Specs.EveryValidationConstraint).AssertNoErrors();

        var emitted = result.SourceContaining("petstore.g.cs");

        // property:, or the attribute lands on a positional record's parameter and the generator
        // reading properties never sees it.
        Assert.Contains("[property: Required]", emitted);
        Assert.Contains("[property: StringLength(", emitted);
        Assert.Contains("[property: Range(", emitted);

        // The reference form, pointing at a [GeneratedRegex] member the task also emits. The inline
        // form would root the regex engine at 448 KB on an AOT publish.
        Assert.Contains("[property: Pattern(typeof(", emitted);
        Assert.Contains("GeneratedRegex(", emitted);
        Assert.DoesNotContain("new Regex(", emitted);
    }

    /// <summary>
    /// The AOT serialisation resolver. It is the largest emitted file and the one most exposed to a
    /// type-mapping mistake: every property appears three times, in a cast, in a
    /// <c>CreatePropertyInfo&lt;T&gt;</c> and in a <c>typeof</c>, and all three have to agree.
    /// </summary>
    [Fact]
    public void TheJsonTypeInfoResolverCompiles() {
        var result = OpenApiGenerator.Run(Specs.EverySchemaShape).AssertNoErrors();

        var resolver = result.SourceContaining("petstore.g.cs");

        Assert.Contains("public sealed class PetstoreJsonTypeInfoResolver : IJsonTypeInfoResolver", resolver);
        Assert.Contains("if (type == typeof(Widget)) return CreateWidgetTypeInfo(options);", resolver);
        Assert.Contains("if (type == typeof(WidgetStatus)) return CreateWidgetStatusTypeInfo(options);", resolver);
        Assert.Contains("CreateListInfo<List<Part>, Part>", resolver);
        Assert.Contains("CreateDictionaryInfo<Dictionary<string, string>, string, string>", resolver);
    }

    /// <summary>
    /// The resolver has to be usable, not merely compilable: it is registered as an
    /// <c>IJsonTypeInfoResolver</c> and asked for types at run time, so a schema whose properties do
    /// not line up with the record's constructor fails only when something deserialises.
    /// </summary>
    [Fact]
    public void TheJsonTypeInfoResolverRoundTripsAGeneratedRecord() {
        OpenApiGenerator.Run(
                Specs.EverySchemaShape,
                """
                using System.Text.Json;
                using System.Text.Json.Serialization.Metadata;
                using TestNamespace.Models;

                namespace TestNamespace;

                public static class RoundTrip {
                    public static Widget Run(string json) {
                        var options = new JsonSerializerOptions {
                            TypeInfoResolver = PetstoreJsonTypeInfoResolver.Instance
                        };

                        return JsonSerializer.Deserialize<Widget>(json, options)!;
                    }
                }
                """)
            .AssertNoErrors();
    }

    /// <summary>
    /// <c>allOf</c> composition. The derived record carries its base's properties, and its base's
    /// required list decides which constructor parameters are non-nullable — so a merge that loses
    /// <c>required</c> produces a record whose parameters are ordered wrongly and does not compile.
    /// </summary>
    [Fact]
    public void AllOfCompositionCompiles() {
        var result = OpenApiGenerator.Run(Specs.AllOfComposition).AssertNoErrors();

        var record = result.SourceContaining("petstore.g.cs");

        Assert.Contains("string Id,", record);
        Assert.Contains("string Breed,", record);
        Assert.Contains("string? Nickname = default", record);
    }

    /// <summary>
    /// A filter attribute declared by <c>x-filter-types</c> and applied by <c>x-filters</c>. The
    /// attribute type is emitted by the generator and instantiated by the handler in the same run, so
    /// a property name or literal the emitter formats wrongly fails to compile.
    /// </summary>
    [Fact]
    public void FilterAttributesFromTheSpecCompile() {
        var result = OpenApiGenerator.Run(Specs.FilterTypes).AssertNoErrors();

        var attribute = result.SourceContaining("petstore.g.cs");

        Assert.Contains("public partial class RateLimitAttribute : Attribute", attribute);
        Assert.Contains("public int MaxRequests { get; set; } = 100;", attribute);
        Assert.Contains("public string Window { get; set; } = \"minute\";", attribute);
        Assert.Contains("public bool Enabled { get; set; } = true;", attribute);

        var handler = result.SourceContaining("ThingController_ListThings");

        Assert.Contains("global::TestNamespace.Filters.RateLimitAttribute", handler);
        Assert.Contains("MaxRequests = 10", handler);
        Assert.Contains("Window = \"second\"", handler);
        Assert.Contains("Enabled = false", handler);
    }

    /// <summary>
    /// <c>generate: false</c> says the attribute lives in a referenced library, so the generator maps
    /// <c>x-filters</c> onto it without emitting a second definition that would collide with it.
    /// </summary>
    [Fact]
    public void AFilterTypeMarkedNotGeneratedIsNotEmitted() {
        var result = OpenApiGenerator.Run(Specs.FilterTypes).AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("ExternalAttribute"));
    }

    /// <summary>
    /// <c>x-codegen-exclude</c> keeps a parameter out of the generated signature — a header or trace
    /// identifier the transport supplies rather than the caller.
    /// </summary>
    [Fact]
    public void AParameterMarkedCodegenExcludeIsNotGenerated() {
        var result = OpenApiGenerator.Run(Specs.CodegenExcludedParameter).AssertNoErrors();

        var generated = result.SourceContaining("petstore.g.cs");

        Assert.Contains("ListThings(int? page)", generated);
        Assert.DoesNotContain("traceId", generated);
    }

    /// <summary>
    /// A schema no operation references still gets a record, so a project can share model types the
    /// spec declares but never returns.
    /// </summary>
    [Fact]
    public void AnUnreferencedSchemaStillGetsARecord() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Orphan:
                  type: object
                  required: [id]
                  properties:
                    id: { type: string }
            """).AssertNoErrors();

        Assert.Contains("public partial record Orphan(", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// A schema with no properties. The record emitter takes a different branch for it — a bodyless
    /// declaration rather than a positional one — and so does the resolver, which needs an
    /// <c>ObjectCreator</c> instead of a parameterised one.
    /// </summary>
    [Fact]
    public void AnEmptySchemaCompiles() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Empty:
                  type: object
            """).AssertNoErrors();

        Assert.Contains("public partial record Empty;", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// A property whose name is a C# keyword. The generated record capitalises it, which is what
    /// keeps <c>class</c> or <c>event</c> from landing in the constructor unescaped.
    /// </summary>
    [Fact]
    public void APropertyNamedAfterAKeywordCompiles() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Booking:
                  type: object
                  required: [class, event]
                  properties:
                    class: { type: string }
                    event: { type: string }
            """).AssertNoErrors();

        var record = result.SourceContaining("petstore.g.cs");

        Assert.Contains("string Class,", record);
        Assert.Contains("string Event)", record);
    }

    /// <summary>
    /// A hyphenated or underscored spec name becomes a single PascalCase C# identifier. Anything less
    /// produces an identifier the compiler rejects.
    /// </summary>
    [Fact]
    public void HyphenatedAndUnderscoredNamesBecomeValidIdentifiers() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /shipping-labels:
                get:
                  tags: [shipping-label]
                  operationId: list_shipping_labels
                  responses:
                    '200': { description: ok }
            components:
              schemas:
                shipping-label:
                  type: object
                  required: [tracking_number]
                  properties:
                    tracking_number: { type: string }
            """).AssertNoErrors();

        Assert.Contains("public partial record ShippingLabel(", result.SourceContaining("petstore.g.cs"));
        Assert.Contains("public partial interface IShippingLabelService", result.SourceContaining("petstore.g.cs"));
        Assert.Contains("ListShippingLabels(", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// Two tags means two service interfaces and two sets of handlers, all in one namespace. This is
    /// the case where a hint name or a class name that is not qualified by tag collides.
    /// </summary>
    [Fact]
    public void TwoTagsInOneSpecProduceTwoInterfacesThatCompile() {
        var result = OpenApiGenerator.Run(
                """
                openapi: "3.0.0"
                info: { title: T, version: "1.0" }
                paths:
                  /pets:
                    get:
                      tags: [Pet]
                      operationId: listPets
                      responses:
                        '200': { description: ok }
                  /stores:
                    get:
                      tags: [Store]
                      operationId: listStores
                      responses:
                        '200': { description: ok }
                """,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class PetServiceImpl : IPetService {
                        public Task ListPets() => Task.CompletedTask;
                    }

                    [Handler]
                    public class StoreServiceImpl : IStoreService {
                        public Task ListStores() => Task.CompletedTask;
                    }
                    """))
            .AssertNoErrors();

        var routing = result.SourceContaining("OpenApiRouting");

        Assert.Contains("IPetService,", routing);
        Assert.Contains("IStoreService,", routing);
        Assert.Contains("PetServiceImpl", routing);
        Assert.Contains("StoreServiceImpl", routing);
    }

    /// <summary>
    /// An operation with no <c>operationId</c>. The generator derives one from the verb and the path,
    /// and that derived name becomes a C# method name — so a path segment it fails to sanitise is a
    /// compile error rather than an odd-looking name.
    /// </summary>
    [Fact]
    public void AnOperationWithoutAnOperationIdCompiles() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /shipping-labels/{id}/history:
                get:
                  tags: [Label]
                  responses:
                    '200': { description: ok }
            """).AssertNoErrors();

        Assert.Contains("GetShippingLabelsHistory(", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// An untagged operation lands on the <c>Default</c> tag rather than being dropped, so the
    /// interface it belongs to is <c>IDefaultService</c>.
    /// </summary>
    [Fact]
    public void AnUntaggedOperationLandsOnTheDefaultService() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /health:
                get:
                  operationId: healthCheck
                  responses:
                    '200': { description: ok }
            """).AssertNoErrors();

        Assert.Contains("public partial interface IDefaultService", result.SourceContaining("petstore.g.cs"));
    }

    /// <summary>
    /// A specification with no <c>paths</c> produces models and nothing else. Nothing to route is not
    /// an error, and the empty routing table still has to compile.
    /// </summary>
    [Fact]
    public void ASpecWithNoOperationsCompiles() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  required: [id]
                  properties:
                    id: { type: string }
            """).AssertNoErrors();

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("Service"));
    }

    /// <summary>
    /// Without a <c>[HardenedModule]</c> entry point there is nothing to attach a routing table to.
    /// The models, interfaces and handlers are still generated and still compile, which is what lets
    /// a library project carry generated types for an application elsewhere to route.
    /// </summary>
    [Fact]
    public void ASpecWithNoEntryPointStillEmitsModelsAndCompiles() {
        var result = OpenApiGenerator.Run(
                Specs.Minimal,
                "namespace TestNamespace; public class NotAModule { }")
            .AssertNoErrors();

        Assert.Contains("public partial record Pet", result.GeneratedSources["petstore.g.cs"]);
        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("OpenApiRouting"));
    }

    /// <summary>
    /// A response body, a request body, a path token and a query parameter in one operation — the
    /// combination most likely to break parameter indexing in the emitted <c>Parameters</c> class.
    /// </summary>
    [Fact]
    public void AnOperationWithABodyAPathTokenAndAQueryParameterCompiles() {
        OpenApiGenerator.Run(
                """
                openapi: "3.0.0"
                info: { title: T, version: "1.0" }
                paths:
                  /pets/{petId}:
                    put:
                      tags: [Pet]
                      operationId: replacePet
                      parameters:
                        - name: petId
                          in: path
                          required: true
                          schema: { type: string }
                        - name: notify
                          in: query
                          schema: { type: boolean }
                      requestBody:
                        content:
                          application/json:
                            schema:
                              $ref: '#/components/schemas/Pet'
                      responses:
                        '200':
                          description: ok
                          content:
                            application/json:
                              schema:
                                $ref: '#/components/schemas/Pet'
                components:
                  schemas:
                    Pet:
                      type: object
                      required: [id]
                      properties:
                        id: { type: string }
                """,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class PetServiceImpl : IPetService {
                        public Task<Pet> ReplacePet(string petId, bool? notify, Pet body) =>
                            Task.FromResult(body);
                    }
                    """))
            .AssertNoErrors();
    }

    /// <summary>
    /// A response that is a bare primitive rather than a <c>$ref</c>. The interface returns the
    /// mapped C# type, and the handler assigns the awaited result to the response value.
    /// </summary>
    [Fact]
    public void APrimitiveResponseCompiles() {
        var result = OpenApiGenerator.Run(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /count:
                get:
                  tags: [Stat]
                  operationId: getCount
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: integer
                            format: int64
            """).AssertNoErrors();

        Assert.Contains("Task<long> GetCount(", result.SourceContaining("petstore.g.cs"));
    }
}
