using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What the spec model's equality is for.
///
/// <para>
/// <see cref="OpenApiSpecModel"/> and everything it holds sit inside Roslyn's incremental pipeline:
/// <c>OpenApiSourceGenerator</c> parses each <c>AdditionalFiles</c> entry into one, and Roslyn
/// compares the new value against the cached one to decide whether the downstream emit runs again.
/// Equality that is too loose serves stale generated code after a real edit; equality that is too
/// tight regenerates every type in the project on every keystroke.
/// </para>
///
/// <para>
/// Every model here is produced by <see cref="OpenApiSpecParser.Parse"/> from a document a project
/// could ship, rather than assembled by hand. A model built in a test can express shapes the parser
/// never produces, and equality that only agrees with such a model proves nothing about the pipeline
/// it actually runs in.
/// </para>
/// </summary>
public class SpecModelEqualityTests {

    private static OpenApiSpecModel Parse(string yaml, string fileName = "spec") {
        var model = OpenApiSpecParser.Parse(yaml, fileName, CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    /// <summary>
    /// A document parsed twice compares equal all the way down — schemas, services, operations,
    /// parameters, body properties and filter types. This is the case the incremental pipeline hits
    /// on every rebuild where the spec did not change, and the one that decides whether the
    /// generator re-emits the whole project.
    /// </summary>
    [Theory]
    [InlineData(nameof(Specs.Minimal))]
    [InlineData(nameof(Specs.EverySchemaShape))]
    [InlineData(nameof(Specs.EveryValidationConstraint))]
    [InlineData(nameof(Specs.AllOfComposition))]
    [InlineData(nameof(Specs.EveryVerb))]
    [InlineData(nameof(Specs.FilterTypes))]
    [InlineData(nameof(Specs.CodegenExcludedParameter))]
    public void TheSameDocumentParsedTwiceComparesEqual(string specName) {
        var yaml = SpecByName(specName);

        Assert.Equal(Parse(yaml), Parse(yaml));
    }

    /// <summary>
    /// Equal models hash equal. Roslyn keys cached values by hash before it compares them, so a hash
    /// that disagrees with <c>Equals</c> makes the comparison unreachable.
    /// </summary>
    [Theory]
    [InlineData(nameof(Specs.Minimal))]
    [InlineData(nameof(Specs.EverySchemaShape))]
    [InlineData(nameof(Specs.EveryValidationConstraint))]
    [InlineData(nameof(Specs.FilterTypes))]
    public void EqualDocumentsHashEqual(string specName) {
        var yaml = SpecByName(specName);

        Assert.Equal(Parse(yaml).GetHashCode(), Parse(yaml).GetHashCode());
    }

    /// <summary>
    /// The file name is part of identity. Two specifications with identical content under different
    /// names generate different files — the hint names are prefixed with it — so they must not
    /// collapse into one cache entry.
    /// </summary>
    [Fact]
    public void TheSameContentUnderTwoFileNamesIsTwoModels() {
        Assert.NotEqual(Parse(Specs.Minimal, "pets"), Parse(Specs.Minimal, "stores"));
    }

    [Fact]
    public void ASpecModelIsNotEqualToNull() {
        Assert.False(Parse(Specs.Minimal).Equals(null));
        Assert.False(Parse(Specs.Minimal).Equals((object?)null));
    }

    [Fact]
    public void ASpecModelIsNotEqualToSomethingElseEntirely() {
        Assert.False(Parse(Specs.Minimal).Equals("petstore.yaml"));
    }

    [Fact]
    public void ASpecModelEqualsItself() {
        var model = Parse(Specs.Minimal);

        Assert.True(model.Equals(model));
    }

    // ── what a change to the document has to be noticed as ────────────────────────────────────

    /// <summary>
    /// Adding a schema changes the model. A comparison that stops at the counts it already checked
    /// would still catch this; the point is that the generated record for the new schema depends on
    /// it being caught.
    /// </summary>
    [Fact]
    public void AddingASchemaMakesTheModelUnequal() {
        var before = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  properties:
                    id: { type: string }
            """);

        var after = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  properties:
                    id: { type: string }
                Store:
                  type: object
                  properties:
                    id: { type: string }
            """);

        Assert.NotEqual(before, after);
    }

    /// <summary>
    /// Renaming a property changes the model even though the schema count, the property count and
    /// every type stay the same. The generated record's parameter name comes from it.
    /// </summary>
    [Fact]
    public void RenamingAPropertyMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(SchemaWithOneProperty("id", "string")),
            Parse(SchemaWithOneProperty("identifier", "string")));
    }

    /// <summary>
    /// Changing a property's type changes the model. This is the edit that would otherwise emit a
    /// record with the old C# type against the new JSON shape.
    /// </summary>
    [Fact]
    public void ChangingAPropertyTypeMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(SchemaWithOneProperty("id", "string")),
            Parse(SchemaWithOneProperty("id", "integer")));
    }

    /// <summary>
    /// Moving a property into the <c>required</c> list changes the model: the record's parameter
    /// stops being nullable and a <c>RequiredRule</c> appears in the validation filter.
    /// </summary>
    [Fact]
    public void MakingAPropertyRequiredMakesTheModelUnequal() {
        var optional = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  properties:
                    id: { type: string }
            """);

        var required = Parse(
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
            """);

        Assert.NotEqual(optional, required);
    }

    /// <summary>
    /// Turning an object schema into an enum changes its kind, which decides whether the emitter
    /// writes a record or an enum for it.
    /// </summary>
    [Fact]
    public void ChangingASchemasKindMakesTheModelUnequal() {
        var asObject = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Status:
                  type: object
                  properties:
                    value: { type: string }
            """);

        var asEnum = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Status:
                  type: string
                  enum: [on, off]
            """);

        Assert.NotEqual(asObject, asEnum);
    }

    /// <summary>Reordering an enum's values changes it — the generated members keep the document's order.</summary>
    [Fact]
    public void ReorderingEnumValuesMakesTheModelUnequal() {
        Assert.NotEqual(Parse(EnumSchema("[on, off]")), Parse(EnumSchema("[off, on]")));
    }

    /// <summary>Adding an operation to an existing tag changes that tag's service.</summary>
    [Fact]
    public void AddingAnOperationMakesTheModelUnequal() {
        var before = Parse(OneGetOperation("/pets", "listPets", "Pet"));

        var after = Parse(
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
                post:
                  tags: [Pet]
                  operationId: createPet
                  responses:
                    '201': { description: created }
            """);

        Assert.NotEqual(before, after);
    }

    /// <summary>Retagging an operation moves it to a different service interface.</summary>
    [Fact]
    public void RetaggingAnOperationMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(OneGetOperation("/pets", "listPets", "Pet")),
            Parse(OneGetOperation("/pets", "listPets", "Store")));
    }

    /// <summary>Renaming an operation renames the generated method and its handler class.</summary>
    [Fact]
    public void RenamingAnOperationMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(OneGetOperation("/pets", "listPets", "Pet")),
            Parse(OneGetOperation("/pets", "getAllPets", "Pet")));
    }

    /// <summary>Moving an operation to another path changes the route it is registered under.</summary>
    [Fact]
    public void ChangingAnOperationsPathMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(OneGetOperation("/pets", "listPets", "Pet")),
            Parse(OneGetOperation("/animals", "listPets", "Pet")));
    }

    /// <summary>The verb decides which route-table branch the handler is reached from.</summary>
    [Fact]
    public void ChangingAnOperationsVerbMakesTheModelUnequal() {
        var asGet = Parse(OneGetOperation("/pets", "listPets", "Pet"));

        var asPost = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /pets:
                post:
                  tags: [Pet]
                  operationId: listPets
                  responses:
                    '200': { description: ok }
            """);

        Assert.NotEqual(asGet, asPost);
    }

    // ── parameters ────────────────────────────────────────────────────────────────────────────

    /// <summary>Renaming a parameter renames the bound C# parameter and its query-string key.</summary>
    [Fact]
    public void RenamingAParameterMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(OneQueryParameter("page", "integer", "")),
            Parse(OneQueryParameter("offset", "integer", "")));
    }

    /// <summary>
    /// Moving a parameter from the query string to a header changes how it binds — a different
    /// <c>ParameterBindType</c>, read from a different part of the request.
    /// </summary>
    [Fact]
    public void MovingAParameterToADifferentLocationMakesTheModelUnequal() {
        var inQuery = Parse(OneQueryParameter("page", "integer", ""));

        var inHeader = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /things:
                get:
                  tags: [Thing]
                  operationId: listThings
                  parameters:
                    - name: page
                      in: header
                      schema: { type: integer }
                  responses:
                    '200': { description: ok }
            """);

        Assert.NotEqual(inQuery, inHeader);
    }

    /// <summary>Making a parameter required stops the generated parameter being nullable.</summary>
    [Fact]
    public void MakingAParameterRequiredMakesTheModelUnequal() {
        var optional = Parse(OneQueryParameter("page", "integer", ""));

        var required = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /things:
                get:
                  tags: [Thing]
                  operationId: listThings
                  parameters:
                    - name: page
                      in: query
                      required: true
                      schema: { type: integer }
                  responses:
                    '200': { description: ok }
            """);

        Assert.NotEqual(optional, required);
    }

    /// <summary>
    /// Every validation constraint a parameter can carry, one at a time. Each one becomes a rule in
    /// the emitted filter provider, so a comparison that skipped it would leave the old rules in
    /// place after the spec tightened them.
    /// </summary>
    [Theory]
    [InlineData("minLength: 3")]
    [InlineData("maxLength: 12")]
    [InlineData("minimum: 1")]
    [InlineData("maximum: 100")]
    [InlineData("minimum: 1\n                        exclusiveMinimum: true")]
    [InlineData("maximum: 1\n                        exclusiveMaximum: true")]
    [InlineData("pattern: \"^[a-z]+$\"")]
    public void AddingAConstraintToAParameterMakesTheModelUnequal(string constraint) {
        Assert.NotEqual(
            Parse(OneQueryParameter("code", "string", "")),
            Parse(OneQueryParameter("code", "string", constraint)));
    }

    /// <summary>
    /// Array bounds on a parameter. They reach an <c>ArrayBoundsRule</c> rather than a
    /// <c>RangeRule</c>, and are compared separately from the numeric bounds.
    /// </summary>
    [Theory]
    [InlineData("minItems: 1")]
    [InlineData("maxItems: 10")]
    public void AddingArrayBoundsToAParameterMakesTheModelUnequal(string constraint) {
        var plain = Parse(ArrayQueryParameter(""));
        var bounded = Parse(ArrayQueryParameter(constraint));

        Assert.NotEqual(plain, bounded);
    }

    // ── request bodies ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A constraint on a body property. It reaches <c>OperationModel.RequestBodyProperties</c>, which
    /// is compared separately from the schema the body references.
    /// </summary>
    [Theory]
    [InlineData("minLength: 3")]
    [InlineData("maxLength: 12")]
    [InlineData("pattern: \"^[A-Z]+$\"")]
    [InlineData("minimum: 1")]
    [InlineData("maximum: 9")]
    public void AddingAConstraintToABodyPropertyMakesTheModelUnequal(string constraint) {
        Assert.NotEqual(Parse(BodyWithProperty("")), Parse(BodyWithProperty(constraint)));
    }

    /// <summary>
    /// The body's required list. It becomes a <c>RequiredRule</c> on the property rather than only a
    /// non-nullable record parameter, so it is carried on the operation as well as on the schema.
    /// </summary>
    [Fact]
    public void ChangingTheBodysRequiredListMakesTheModelUnequal() {
        var noneRequired = Parse(BodyWithProperty(""));

        var skuRequired = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /orders:
                post:
                  tags: [Order]
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/CreateOrderRequest'
                  responses:
                    '201': { description: created }
            components:
              schemas:
                CreateOrderRequest:
                  type: object
                  required: [sku]
                  properties:
                    sku:
                      type: string
            """);

        Assert.NotEqual(noneRequired, skuRequired);
    }

    // ── x-filters and x-filter-types ──────────────────────────────────────────────────────────

    /// <summary>
    /// Changing a value passed to an <c>x-filters</c> instance changes the property assignment
    /// emitted on the attribute — the whole point of the extension.
    /// </summary>
    [Fact]
    public void ChangingAFilterInstancesPropertyValueMakesTheModelUnequal() {
        Assert.NotEqual(Parse(FilterInstanceWith("10")), Parse(FilterInstanceWith("20")));
    }

    /// <summary>Applying a second filter to an operation changes it.</summary>
    [Fact]
    public void AddingASecondFilterInstanceMakesTheModelUnequal() {
        var one = Parse(FilterInstanceWith("10"));

        var two = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            x-filter-types:
              RateLimit:
                namespace: TestNamespace.Filters
                properties:
                  MaxRequests: { type: integer, default: 100 }
              Audit:
                namespace: TestNamespace.Filters
                properties:
                  Level: { type: string, default: "info" }
            paths:
              /things:
                get:
                  tags: [Thing]
                  operationId: listThings
                  x-filters:
                    RateLimit:
                      MaxRequests: 10
                    Audit:
                      Level: debug
                  responses:
                    '200': { description: ok }
            """);

        Assert.NotEqual(one, two);
    }

    /// <summary>
    /// A filter type's namespace decides where the attribute is emitted and how <c>x-filters</c>
    /// qualifies it, so it is part of the type's identity.
    /// </summary>
    [Fact]
    public void ChangingAFilterTypesNamespaceMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(FilterTypeIn("TestNamespace.Filters")),
            Parse(FilterTypeIn("OtherNamespace.Filters")));
    }

    /// <summary>Adding a property to a filter type adds a property to the emitted attribute.</summary>
    [Fact]
    public void AddingAPropertyToAFilterTypeMakesTheModelUnequal() {
        var oneProperty = Parse(FilterTypeIn("TestNamespace.Filters"));

        var twoProperties = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            x-filter-types:
              RateLimit:
                namespace: TestNamespace.Filters
                properties:
                  MaxRequests: { type: integer, default: 100 }
                  Window: { type: string, default: "minute" }
            paths: {}
            """);

        Assert.NotEqual(oneProperty, twoProperties);
    }

    /// <summary>
    /// A filter property's default is emitted as the attribute property's initialiser, so changing it
    /// changes generated code.
    /// </summary>
    [Fact]
    public void ChangingAFilterPropertysDefaultMakesTheModelUnequal() {
        var hundred = Parse(FilterTypeIn("TestNamespace.Filters"));

        var fifty = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            x-filter-types:
              RateLimit:
                namespace: TestNamespace.Filters
                properties:
                  MaxRequests: { type: integer, default: 50 }
            paths: {}
            """);

        Assert.NotEqual(hundred, fifty);
    }

    /// <summary>
    /// <c>FullTypeName</c> is what <c>x-filters</c> resolves an instance to, and what the emitted
    /// attribute's file is named after.
    /// </summary>
    [Fact]
    public void AFilterTypesFullNameCombinesItsNamespaceAndPascalCasedName() {
        var filterType = Assert.Single(Parse(FilterTypeIn("TestNamespace.Filters")).FilterTypes);

        Assert.Equal("RateLimitAttribute", filterType.ClassName);
        Assert.Equal("TestNamespace.Filters.RateLimitAttribute", filterType.FullTypeName);
    }

    // ── the pipeline this equality exists for ─────────────────────────────────────────────────

    /// <summary>
    /// The behaviour all of the above is in service of: an edit to C# that cannot affect the
    /// specification leaves every generated file byte-identical. Roslyn reaches that answer by
    /// comparing the parsed spec models, so a comparison that reported a difference here would
    /// regenerate the whole project on every keystroke.
    /// </summary>
    [Fact]
    public void AnEditThatCannotAffectTheSpecRegeneratesIdenticalOutput() {
        var result = GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> { ["Test.cs"] = OpenApiGenerator.MinimalEntryPoint },
            new Dictionary<string, string> {
                ["Test.cs"] = OpenApiGenerator.MinimalEntryPoint + Environment.NewLine + "// a comment"
            },
            [new OpenApiSourceGenerator()],
            null,
            new Dictionary<string, string> { ["petstore.yaml"] = Specs.EverySchemaShape });

        Assert.NotEmpty(result.FirstRun);
        Assert.Equal(
            result.FirstRun.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            result.SecondRun.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    // ── documents ─────────────────────────────────────────────────────────────────────────────

    private static string SpecByName(string name) => name switch {
        nameof(Specs.Minimal) => Specs.Minimal,
        nameof(Specs.EverySchemaShape) => Specs.EverySchemaShape,
        nameof(Specs.EveryValidationConstraint) => Specs.EveryValidationConstraint,
        nameof(Specs.AllOfComposition) => Specs.AllOfComposition,
        nameof(Specs.EveryVerb) => Specs.EveryVerb,
        nameof(Specs.FilterTypes) => Specs.FilterTypes,
        nameof(Specs.CodegenExcludedParameter) => Specs.CodegenExcludedParameter,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No such document")
    };

    private static string SchemaWithOneProperty(string propertyName, string type) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths: {}
        components:
          schemas:
            Pet:
              type: object
              properties:
                {{propertyName}}: { type: {{type}} }
        """;

    private static string EnumSchema(string values) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths: {}
        components:
          schemas:
            Status:
              type: string
              enum: {{values}}
        """;

    private static string OneGetOperation(string path, string operationId, string tag) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths:
          {{path}}:
            get:
              tags: [{{tag}}]
              operationId: {{operationId}}
              responses:
                '200': { description: ok }
        """;

    private static string OneQueryParameter(string name, string type, string constraint) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              parameters:
                - name: {{name}}
                  in: query
                  schema:
                    type: {{type}}
                    {{constraint}}
              responses:
                '200': { description: ok }
        """;

    private static string ArrayQueryParameter(string constraint) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              parameters:
                - name: skus
                  in: query
                  schema:
                    type: array
                    items: { type: string }
                    {{constraint}}
              responses:
                '200': { description: ok }
        """;

    private static string BodyWithProperty(string constraint) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths:
          /orders:
            post:
              tags: [Order]
              operationId: createOrder
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/CreateOrderRequest'
              responses:
                '201': { description: created }
        components:
          schemas:
            CreateOrderRequest:
              type: object
              properties:
                sku:
                  type: string
                  {{constraint}}
        """;

    private static string FilterInstanceWith(string maxRequests) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        x-filter-types:
          RateLimit:
            namespace: TestNamespace.Filters
            properties:
              MaxRequests: { type: integer, default: 100 }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              x-filters:
                RateLimit:
                  MaxRequests: {{maxRequests}}
              responses:
                '200': { description: ok }
        """;

    private static string FilterTypeIn(string ns) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        x-filter-types:
          RateLimit:
            namespace: {{ns}}
            properties:
              MaxRequests: { type: integer, default: 100 }
        paths: {}
        """;

    // ── responses ──────────────────────────────────────────────────────
    //
    // OperationModel.Equals compared the operation's identity and its parameters and nothing else,
    // so every edit below produced a model that compared equal to the one before it. In the
    // incremental pipeline that means the downstream emit does not rerun and the build keeps
    // serving generated code the spec no longer describes. None of these were covered.

    /// <summary>
    /// Changing the media type a response is declared under changes what the handler writes -
    /// it is what decides between the JSON serializer and the raw writer.
    /// </summary>
    [Fact]
    public void ChangingAResponsesMediaTypeMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(ResponseOf("application/json", "{ type: string }")),
            Parse(ResponseOf("text/plain", "{ type: string }")));
    }

    /// <summary>Changing the response schema's type changes the generated return type.</summary>
    [Fact]
    public void ChangingAResponseSchemaTypeMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(ResponseOf("application/json", "{ type: string }")),
            Parse(ResponseOf("application/json", "{ type: integer }")));
    }

    /// <summary>
    /// Changing the response schema's format changes the mapped C# type - integer/int64 is long
    /// where integer alone is int.
    /// </summary>
    [Fact]
    public void ChangingAResponseSchemaFormatMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(ResponseOf("application/json", "{ type: integer }")),
            Parse(ResponseOf("application/json", "{ type: integer, format: int64 }")));
    }

    /// <summary>
    /// Turning a response into an array of the same thing changes the return type to a List.
    /// </summary>
    [Fact]
    public void MakingAResponseAnArrayMakesTheModelUnequal() {
        Assert.NotEqual(
            Parse(ResponseOf("application/json", "{ type: string }")),
            Parse(ResponseOf("application/json", "{ type: array, items: { type: string } }")));
    }

    /// <summary>The success status code is emitted onto the handler.</summary>
    [Fact]
    public void ChangingTheSuccessStatusCodeMakesTheModelUnequal() {
        Assert.NotEqual(Parse(SuccessStatus("200")), Parse(SuccessStatus("201")));
    }

    /// <summary>
    /// A request body's media type moves with the same reasoning as a response's.
    /// </summary>
    [Fact]
    public void ChangingARequestBodysMediaTypeMakesTheModelUnequal() {
        Assert.NotEqual(Parse(RequestBodyOf("application/json")), Parse(RequestBodyOf("text/plain")));
    }

    private static string ResponseOf(string mediaType, string schema) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths:
          /things:
            get:
              tags: [Thing]
              operationId: listThings
              responses:
                '200':
                  description: ok
                  content:
                    {{mediaType}}:
                      schema: {{schema}}
        """;

    private static string SuccessStatus(string status) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths:
          /things:
            post:
              tags: [Thing]
              operationId: createThing
              responses:
                '{{status}}': { description: ok }
        """;

    private static string RequestBodyOf(string mediaType) =>
        $$"""
        openapi: "3.0.0"
        info: { title: T, version: "1.0" }
        paths:
          /things:
            post:
              tags: [Thing]
              operationId: createThing
              requestBody:
                content:
                  {{mediaType}}:
                    schema: { type: string }
              responses:
                '200': { description: ok }
        """;
}
