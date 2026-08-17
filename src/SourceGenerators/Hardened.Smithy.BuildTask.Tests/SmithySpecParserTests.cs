using Hardened.Idl;
using Hardened.Idl.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// The AST reader, against a fixture produced by the real <c>smithy ast --flatten</c>.
/// </summary>
/// <remarks>
/// The fixture is generated rather than hand-written, and its <c>.smithy</c> source sits beside it.
/// A hand-written AST would encode what this reader expects rather than what the CLI emits, which
/// is the one thing these tests exist to check - inline <c>input :=</c> structures arriving hoisted
/// and named, enum members carrying both halves, every reference already absolute.
/// </remarks>
public class SmithySpecParserTests {

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static ServiceSpecModel Parse(out List<string> diagnostics) {
        diagnostics = new List<string>();

        var model = SmithySpecParser.Parse(Fixture("petstore.json"), "petstore", diagnostics);

        Assert.NotNull(model);

        return model!;
    }

    private static OperationModel Operation(ServiceSpecModel model, string operationId) =>
        Assert.Single(model.Services[0].Operations, o => o.OperationId == operationId);

    private static SchemaModel Schema(ServiceSpecModel model, string name) =>
        Assert.Single(model.Schemas, s => s.Name == name);

    [Fact]
    public void Parse_ReadsOperationsFromTheServiceShape() {
        var model = Parse(out _);

        var service = Assert.Single(model.Services);

        Assert.Equal("PetStore", service.Tag);
        Assert.Equal(
            new[] { "CreatePet", "GetPet", "ListPets" },
            service.Operations.ConvertAll(o => o.OperationId));
    }

    [Fact]
    public void Parse_ReadsHttpTraitIntoRouteMethodAndStatus() {
        var model = Parse(out _);

        var create = Operation(model, "CreatePet");

        Assert.Equal("/pets", create.Path);
        Assert.Equal("POST", create.HttpMethod);
        Assert.Equal(201, create.SuccessStatusCode);
    }

    [Fact]
    public void Parse_ReadsDocumentationAsDescription() {
        var model = Parse(out _);

        Assert.Equal("Fetch one pet by id.", Operation(model, "GetPet").Description);
    }

    [Fact]
    public void Parse_BindsHttpLabelAsPathParameter() {
        var model = Parse(out _);

        var parameter = Assert.Single(Operation(model, "GetPet").Parameters, p => p.Name == "petId");

        Assert.Equal("path", parameter.In);
        Assert.True(parameter.IsRequired);
        Assert.Equal("string", parameter.Type);
    }

    /// <summary>
    /// <c>@httpQuery("verbose")</c> on a member called <c>detailed</c> binds under the trait's
    /// value, and the C# name follows from it.
    /// </summary>
    /// <remarks>
    /// The member's own spelling is deliberately not carried across. <c>MemberNameOverride</c> is
    /// <c>NameAllocator</c>'s output rather than a front end's input - it is allocated from the wire
    /// name in one pass over the whole model, and a parser that set it would be a second naming
    /// authority, which is the defect that pass exists to remove.
    /// </remarks>
    [Fact]
    public void Parse_BindsHttpQueryUnderItsWireName() {
        var model = Parse(out _);

        var parameter = Assert.Single(Operation(model, "GetPet").Parameters, p => p.In == "query");

        Assert.Equal("verbose", parameter.Name);
        Assert.Equal("verbose", parameter.MemberNameOverride);
    }

    [Fact]
    public void Parse_BindsHttpHeaderAsHeaderParameter() {
        var model = Parse(out _);

        var parameter = Assert.Single(Operation(model, "GetPet").Parameters, p => p.In == "header");

        // The header's wire name is not an identifier; the allocator makes one from it.
        Assert.Equal("X-Trace-Id", parameter.Name);
        Assert.Equal("xTraceId", parameter.MemberNameOverride);
    }

    [Fact]
    public void Parse_OperationWithOnlyBoundMembersHasNoRequestBody() {
        var model = Parse(out _);

        Assert.Null(Operation(model, "GetPet").RequestBodyRef);
    }

    /// <summary>
    /// The case OpenAPI needs <c>SynthesizeSchema</c> for. Smithy names the input structure itself,
    /// so the body is that shape and nothing has to be invented.
    /// </summary>
    [Fact]
    public void Parse_UnboundMembersBecomeTheRequestBody() {
        var model = Parse(out _);

        var create = Operation(model, "CreatePet");

        Assert.Equal("application/json", create.RequestBodyContentType);
        Assert.Equal("CreatePetInput", TypeMapper.GetRefName(create.RequestBodyRef!));
        Assert.Contains(create.RequestBodyProperties, p => p.Name == "name");
        Assert.Contains("name", create.RequestBodyRequired);
    }

    [Fact]
    public void Parse_ReadsOutputStructureAsResponse() {
        var model = Parse(out _);

        var get = Operation(model, "GetPet");

        Assert.Equal("GetPetOutput", TypeMapper.GetRefName(get.ResponseRef!));
        Assert.Equal("application/json", get.ResponseContentType);
    }

    [Fact]
    public void Parse_ReadsErrorsWithHttpErrorStatus() {
        var model = Parse(out _);

        var errors = Operation(model, "GetPet").ErrorResponses;

        Assert.Equal(new[] { 404, 429 }, errors.ConvertAll(e => e.StatusCode));
        Assert.Equal("PetNotFound", TypeMapper.GetRefName(errors[0].Ref!));
    }

    /// <summary>
    /// Smithy supplies the C# identifier and the wire value separately, which is the pair the IR
    /// already holds - and the thing OpenAPI has to allocate because it only has the value.
    /// </summary>
    [Fact]
    public void Parse_EnumCarriesBothMemberNameAndWireValue() {
        var model = Parse(out _);

        var kind = Schema(model, "PetKind");

        Assert.Equal(SchemaKind.Enum, kind.Kind);
        Assert.Equal(new[] { "dog", "cat", "other" }, kind.EnumValues);
        Assert.Equal(new[] { "Dog", "Cat", "Other" }, kind.EnumMembers);
    }

    [Fact]
    public void Parse_UnionBecomesAChoiceSchema() {
        var model = Parse(out _);

        var attribute = Schema(model, "Attribute");

        Assert.Equal(SchemaKind.OneOf, attribute.Kind);
        Assert.Equal(2, attribute.OneOf.Count);
        Assert.Contains(attribute.OneOf, b => b.Type == "number" && b.Format == "double");
        Assert.Contains(attribute.OneOf, b => b.Type == "string");
    }

    /// <summary>
    /// A named list is a <c>List&lt;T&gt;</c> at the use site rather than a wrapper type, which is
    /// what <c>InlineNonObjectRefs</c> does on the OpenAPI side.
    /// </summary>
    [Fact]
    public void Parse_NamedListInlinesToAnArrayProperty() {
        var model = Parse(out _);

        var pets = Assert.Single(Schema(model, "ListPetsOutput").Properties, p => p.Name == "pets");

        Assert.True(pets.IsArray);
        Assert.Equal("Pet", TypeMapper.GetRefName(pets.ArrayItemsRef!));
        Assert.DoesNotContain(model.Schemas, s => s.Name == "PetList");
    }

    [Fact]
    public void Parse_NamedMapInlinesToADictionaryProperty() {
        var model = Parse(out _);

        var tags = Assert.Single(Schema(model, "CreatePetInput").Properties, p => p.Name == "tags");

        Assert.True(tags.IsDictionary);
        Assert.Equal("string", tags.DictionaryValueType);
        Assert.DoesNotContain(model.Schemas, s => s.Name == "TagMap");
    }

    [Fact]
    public void Parse_ReadsLengthAsStringBounds() {
        var model = Parse(out _);

        var name = Assert.Single(Schema(model, "CreatePetInput").Properties, p => p.Name == "name");

        Assert.Equal(1, name.MinLength);
        Assert.Equal(64, name.MaxLength);
        Assert.Null(name.MinItems);
    }

    [Fact]
    public void Parse_ReadsRangeAsNumericBounds() {
        var model = Parse(out _);

        var limit = Assert.Single(Operation(model, "ListPets").Parameters, p => p.Name == "limit");

        Assert.Equal(1m, limit.Minimum);
        Assert.Equal(100m, limit.Maximum);
    }

    [Fact]
    public void Parse_ReadsPatternFromTheMember() {
        var model = Parse(out _);

        var petId = Assert.Single(Operation(model, "GetPet").Parameters, p => p.Name == "petId");

        Assert.Equal("^[a-z0-9-]+$", petId.Pattern);
    }

    /// <summary>
    /// <c>@jsonName</c> is what the property is called on the wire; the C# member is named from it
    /// by the allocator, exactly as it would be for an OpenAPI property spelled the same way.
    /// </summary>
    [Fact]
    public void Parse_JsonNameSetsTheWireName() {
        var model = Parse(out _);

        var photo = Assert.Single(
            Schema(model, "CreatePetInput").Properties, p => p.Name == "photo_bytes");

        Assert.Equal("PhotoBytes", photo.MemberNameOverride);
        Assert.Equal("byte", photo.Format);
    }

    [Fact]
    public void Parse_RequiredMemberIsNotNullable() {
        var model = Parse(out _);

        var pet = Schema(model, "Pet");

        Assert.False(Assert.Single(pet.Properties, p => p.Name == "id").IsNullable);
        Assert.True(Assert.Single(pet.Properties, p => p.Name == "nickname").IsNullable);
    }

    [Fact]
    public void Parse_ReadsDeprecatedOntoTheProperty() {
        var model = Parse(out _);

        var pet = Schema(model, "Pet");

        Assert.Contains(pet.Properties, p => p.Name == "nickname");
    }

    [Fact]
    public void Parse_TimestampMapsToDateTimeOffset() {
        var model = Parse(out _);

        var birthday = Assert.Single(
            Schema(model, "CreatePetInput").Properties, p => p.Name == "birthday");

        Assert.Equal("DateTimeOffset", TypeMapper.MapPropertyToCSharpType(birthday));
    }

    [Fact]
    public void Parse_DocumentMapsToJsonElement() {
        var model = Parse(out _);

        var metadata = Assert.Single(Schema(model, "Pet").Properties, p => p.Name == "metadata");

        Assert.Equal("JsonElement", TypeMapper.MapPropertyToCSharpType(metadata));
    }

    [Fact]
    public void Parse_NamesEveryReachedShapeAndNothingElse() {
        var model = Parse(out _);

        // The prelude is never in an AST and must never be generated from one.
        Assert.DoesNotContain(model.Schemas, s => s.Name is "String" or "Boolean" or "Integer");
        Assert.Contains(model.Schemas, s => s.Name == "Pet");
    }

    [Fact]
    public void Parse_EmptyAstExplainsTheRedirectFailure() {
        var diagnostics = new List<string>();

        Assert.Null(SmithySpecParser.Parse("", "empty", diagnostics));
        Assert.Contains(diagnostics, d => d.Contains("stdout"));
    }

    [Fact]
    public void Parse_MalformedJsonIsReportedNotThrown() {
        var diagnostics = new List<string>();

        Assert.Null(SmithySpecParser.Parse("{ not json", "bad", diagnostics));
        Assert.Contains(diagnostics, d => d.Contains("not valid JSON"));
    }

    [Fact]
    public void Parse_ModelWithNoServiceShapeIsReported() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Pet": { "type": "structure", "members": {} } } }
                  """;

        Assert.Null(SmithySpecParser.Parse(ast, "noservice", diagnostics));
        Assert.Contains(diagnostics, d => d.Contains("no service shape"));
    }

    /// <summary>
    /// A model that names a protocol whose wire format this does not serve is refused rather than
    /// ignored: generating REST routes for awsJson1_1 would be confidently wrong, not merely
    /// incomplete.
    /// </summary>
    [Fact]
    public void Parse_RefusesAProtocolItCannotServe() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Svc": {
                        "type": "service", "version": "1",
                        "traits": { "aws.protocols#awsJson1_1": {} } } } }
                  """;

        Assert.Null(SmithySpecParser.Parse(ast, "awsjson", diagnostics));
        Assert.Contains(diagnostics, d => d.Contains("X-Amz-Target"));
    }

    /// <summary>
    /// Absence of a protocol trait is well formed - it is what a hand-written model using only
    /// <c>@http</c> looks like, and the fixture is one.
    /// </summary>
    [Fact]
    public void Parse_AcceptsAModelWithNoProtocolTrait() {
        var model = Parse(out var diagnostics);

        Assert.NotEmpty(model.Services);
        Assert.DoesNotContain(diagnostics, d => d.Contains("does not serve"));
    }

    [Fact]
    public void Parse_SelectsOneServiceByShapeId() {
        var diagnostics = new List<string>();

        var model = SmithySpecParser.Parse(
            Fixture("petstore.json"), "petstore", diagnostics, "com.example.petstore#PetStore");

        Assert.NotNull(model);
        Assert.Single(model!.Services);
    }

    [Fact]
    public void Parse_UnknownServiceShapeIdIsReported() {
        var diagnostics = new List<string>();

        Assert.Null(SmithySpecParser.Parse(
            Fixture("petstore.json"), "petstore", diagnostics, "com.example#Missing"));
        Assert.Contains(diagnostics, d => d.Contains("Missing"));
    }

    /// <summary>
    /// Trait definitions are shapes. A model that declares maven dependencies carries theirs, and
    /// every one would otherwise become a record named after someone else's trait.
    /// </summary>
    [Fact]
    public void Parse_SkipsTraitDefinitionShapes() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Svc": {
                        "type": "service", "version": "1",
                        "operations": [ { "target": "com.example#Op" } ] },
                      "com.example#Op": {
                        "type": "operation",
                        "traits": { "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 } } },
                      "aws.api#arn": {
                        "type": "structure", "members": {},
                        "traits": { "smithy.api#trait": {} } } } }
                  """;

        var model = SmithySpecParser.Parse(ast, "traits", diagnostics);

        Assert.NotNull(model);
        Assert.DoesNotContain(model!.Schemas, s => s.Name == "arn");
    }

    [Fact]
    public void Parse_StreamingOperationIsSkippedAndReported() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Svc": {
                        "type": "service", "version": "1",
                        "operations": [ { "target": "com.example#Ok" }, { "target": "com.example#Stream" } ] },
                      "com.example#Ok": {
                        "type": "operation",
                        "traits": { "smithy.api#http": { "method": "GET", "uri": "/ok", "code": 200 } } },
                      "com.example#Stream": {
                        "type": "operation",
                        "traits": {
                          "smithy.api#http": { "method": "GET", "uri": "/s", "code": 200 },
                          "smithy.api#streaming": {} } } } }
                  """;

        var model = SmithySpecParser.Parse(ast, "streaming", diagnostics);

        Assert.NotNull(model);
        Assert.Single(model!.Services[0].Operations);
        Assert.Contains(diagnostics, d => d.Contains("@streaming"));
    }

    /// <summary>
    /// The point of an allowlist rather than a blanket ignore: a prelude trait nobody classified is
    /// named at build time instead of quietly changing nothing.
    /// </summary>
    [Fact]
    public void Parse_ReportsAPreludeTraitItDoesNotModel() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Svc": {
                        "type": "service", "version": "1",
                        "operations": [ { "target": "com.example#Op" } ] },
                      "com.example#Op": {
                        "type": "operation",
                        "traits": {
                          "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 },
                          "smithy.api#unknownFutureTrait": {} } } } }
                  """;

        Assert.NotNull(SmithySpecParser.Parse(ast, "unknown", diagnostics));
        Assert.Contains(diagnostics, d => d.Contains("unknownFutureTrait"));
    }

    [Fact]
    public void Parse_DoesNotReportCustomTraitsAsUnmodelled() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Svc": {
                        "type": "service", "version": "1",
                        "operations": [ { "target": "com.example#Op" } ] },
                      "com.example#Op": {
                        "type": "operation",
                        "traits": {
                          "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 },
                          "com.example#myOwnTrait": {} } } } }
                  """;

        Assert.NotNull(SmithySpecParser.Parse(ast, "custom", diagnostics));
        Assert.DoesNotContain(diagnostics, d => d.Contains("myOwnTrait"));
    }

    [Fact]
    public void Parse_ResourceLifecycleOperationsFlattenIntoTheService() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Svc": {
                        "type": "service", "version": "1",
                        "resources": [ { "target": "com.example#Thing" } ] },
                      "com.example#Thing": {
                        "type": "resource",
                        "read": { "target": "com.example#GetThing" } },
                      "com.example#GetThing": {
                        "type": "operation",
                        "traits": { "smithy.api#http": { "method": "GET", "uri": "/t", "code": 200 } } } } }
                  """;

        var model = SmithySpecParser.Parse(ast, "resource", diagnostics);

        Assert.NotNull(model);
        Assert.Equal("GetThing", Assert.Single(model!.Services[0].Operations).OperationId);
    }

    /// <summary>
    /// A shape that reaches itself terminates.
    /// </summary>
    /// <remarks>
    /// The reference walk builds a schema the first time it reaches a shape, and recursion is
    /// ordinary in a description - a tree node holding its own children. It terminates because the
    /// shape is marked as built before its members are walked rather than after; the other order
    /// hangs the build with no diagnostic, which is the worst way for this to fail.
    /// </remarks>
    [Fact]
    public void Parse_TerminatesOnASelfReferencingShape() {
        var diagnostics = new List<string>();

        var ast = """
                  { "smithy": "2.0", "shapes": {
                      "com.example#Svc": {
                        "type": "service", "version": "1",
                        "operations": [ { "target": "com.example#Op" } ] },
                      "com.example#Op": {
                        "type": "operation",
                        "output": { "target": "com.example#Node" },
                        "traits": { "smithy.api#http": { "method": "GET", "uri": "/n", "code": 200 } } },
                      "com.example#Node": {
                        "type": "structure",
                        "members": {
                          "name": { "target": "smithy.api#String" },
                          "parent": { "target": "com.example#Node" },
                          "children": { "target": "com.example#NodeList" } } },
                      "com.example#NodeList": {
                        "type": "list", "member": { "target": "com.example#Node" } } } }
                  """;

        var model = SmithySpecParser.Parse(ast, "recursive", diagnostics);

        Assert.NotNull(model);

        var node = Assert.Single(model!.Schemas, s => s.Name == "Node");

        Assert.Equal("Node", TypeMapper.GetRefName(
            Assert.Single(node.Properties, p => p.Name == "parent").Ref!));
        Assert.True(Assert.Single(node.Properties, p => p.Name == "children").IsArray);
    }

    /// <summary>
    /// The model file is byte-compared to decide whether to rewrite it, so a reordered AST must
    /// still produce identical output or every build looks dirty.
    /// </summary>
    [Fact]
    public void Parse_IsStableAcrossRuns() {
        var first = SmithySpecParser.Parse(Fixture("petstore.json"), "petstore", new List<string>());
        var second = SmithySpecParser.Parse(Fixture("petstore.json"), "petstore", new List<string>());

        Assert.Equal(SpecModelSerializer.Write(first!), SpecModelSerializer.Write(second!));
    }
}
