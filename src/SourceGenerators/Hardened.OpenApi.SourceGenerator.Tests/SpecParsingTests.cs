using Hardened.Idl.Models;
using Xunit;
using Hardened.Idl;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What the parser makes of the constructs a real specification uses. Every case goes through
/// <see cref="OpenApiSpecParser.Parse"/> — the same entry point the generator calls — rather than
/// through a hand-built model, so a shape asserted here is one a spec can actually produce.
/// </summary>
public class SpecParsingTests {

    private static ServiceSpecModel Parse(string yaml) {
        var model = OpenApiSpecParser.Parse(yaml, "test", CancellationToken.None);

        Assert.NotNull(model);

        return model!;
    }

    // ── references ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>$ref</c> to an object schema is kept, because that object gets a generated C# type the
    /// property can be typed as.
    /// </summary>
    [Fact]
    public void ARefToAnObjectSchemaIsKept() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Address:
                  type: object
                  properties:
                    city: { type: string }
                Person:
                  type: object
                  properties:
                    address:
                      $ref: '#/components/schemas/Address'
            """);

        var address = model.Schemas.First(s => s.Name == "Person").Properties.First(p => p.Name == "address");

        Assert.Equal("#/components/schemas/Address", address.Ref);
    }

    /// <summary>
    /// A <c>$ref</c> to a primitive alias is inlined instead. Keeping it would make the property type
    /// a C# class that never gets generated, so the underlying type is used.
    /// </summary>
    [Fact]
    public void ARefToAPrimitiveAliasIsInlinedToItsUnderlyingType() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                CustomId:
                  type: string
                Person:
                  type: object
                  properties:
                    id:
                      $ref: '#/components/schemas/CustomId'
            """);

        var id = model.Schemas.First(s => s.Name == "Person").Properties.First(p => p.Name == "id");

        Assert.Null(id.Ref);
        Assert.Equal("string", id.Type);
    }

    /// <summary>A <c>$ref</c> to an enum is kept — an enum does get a generated C# type.</summary>
    [Fact]
    public void ARefToAnEnumIsKept() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Status:
                  type: string
                  enum: [on, off]
                Device:
                  type: object
                  properties:
                    status:
                      $ref: '#/components/schemas/Status'
            """);

        var status = model.Schemas.First(s => s.Name == "Device").Properties.First(p => p.Name == "status");

        Assert.Equal("#/components/schemas/Status", status.Ref);
    }

    [Fact]
    public void AnArrayOfRefsCarriesTheItemReference() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Part:
                  type: object
                  properties:
                    sku: { type: string }
                Widget:
                  type: object
                  properties:
                    parts:
                      type: array
                      items:
                        $ref: '#/components/schemas/Part'
            """);

        var parts = model.Schemas.First(s => s.Name == "Widget").Properties.First(p => p.Name == "parts");

        Assert.True(parts.IsArray);
        Assert.Equal("#/components/schemas/Part", parts.ArrayItemsRef);
    }

    [Fact]
    public void ATopLevelArraySchemaCarriesItsItemReference() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  properties:
                    name: { type: string }
                PetList:
                  type: array
                  items:
                    $ref: '#/components/schemas/Pet'
            """);

        var list = model.Schemas.First(s => s.Name == "PetList");

        Assert.Equal(SchemaKind.Array, list.Kind);
        Assert.Equal("#/components/schemas/Pet", list.ArrayItemsRef);
    }

    /// <summary>
    /// An array of primitives has no reference to carry, so the item type is recorded instead and the
    /// mapper turns it into <c>List&lt;string&gt;</c>.
    /// </summary>
    [Fact]
    public void AnArrayOfPrimitivesCarriesItsItemType() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Widget:
                  type: object
                  properties:
                    tags:
                      type: array
                      items: { type: string }
            """);

        var tags = model.Schemas.First(s => s.Name == "Widget").Properties.First(p => p.Name == "tags");

        Assert.True(tags.IsArray);
        Assert.Null(tags.ArrayItemsRef);
        Assert.Equal("string", tags.ArrayItemsType);
    }

    // ── composition ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>allOf</c> flattens: the derived schema ends up with its base's properties and its base's
    /// required list as well as its own.
    /// </summary>
    [Fact]
    public void AllOfMergesTheBaseSchemasPropertiesAndRequiredList() {
        var model = Parse(Specs.AllOfComposition);

        var dog = model.Schemas.First(s => s.Name == "Dog");

        Assert.Equal(SchemaKind.Object, dog.Kind);
        Assert.Contains(dog.Properties, p => p.Name == "id" && p.IsRequired);
        Assert.Contains(dog.Properties, p => p.Name == "breed" && p.IsRequired);
        Assert.Contains(dog.Properties, p => p.Name == "nickname" && !p.IsRequired);
    }

    /// <summary>
    /// Three levels of <c>allOf</c>. Each contributes its own properties, so a merge that stops after
    /// the first entry loses the middle layer.
    /// </summary>
    [Fact]
    public void AllOfMergesEveryBranchNotOnlyTheFirst() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                A:
                  type: object
                  properties:
                    a: { type: string }
                B:
                  type: object
                  properties:
                    b: { type: string }
                C:
                  allOf:
                    - $ref: '#/components/schemas/A'
                    - $ref: '#/components/schemas/B'
                    - type: object
                      properties:
                        c: { type: string }
            """);

        var c = model.Schemas.First(s => s.Name == "C");

        Assert.Equal(3, c.Properties.Count);
        Assert.Contains(c.Properties, p => p.Name == "a");
        Assert.Contains(c.Properties, p => p.Name == "b");
        Assert.Contains(c.Properties, p => p.Name == "c");
    }

    /// <summary>
    /// A component whose whole definition is an undiscriminated <c>oneOf</c> becomes the type that
    /// holds one of its branches, and the property naming it is typed as that rather than as
    /// <c>JsonElement</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This asserted <c>Primitive</c> and <c>JsonElement</c>, on the reasoning that without a
    /// discriminator there is nothing to switch on at run time. <c>ChoiceResolution</c> answered
    /// that afterwards - a branch carrying a property no other declares is told apart by the
    /// schemas, and branches nothing separates are decided by reading the payload into each and
    /// requiring exactly one to fit - but only for a <c>oneOf</c> declared on a property. A
    /// component declaring the same thing kept the old answer, which is the inconsistency this
    /// closes rather than a boundary anyone chose. <c>Card</c> and <c>Cash</c> here are separated by
    /// their own properties, so no payload is guessed at.
    /// </para>
    /// <para>
    /// A discriminated <c>oneOf</c> that also carries properties of its own is still a hierarchy
    /// base - see <see cref="ADiscriminatedOneOfBecomesAPolymorphicBase"/>. That is the boundary,
    /// and it is about whether the schema declares members, not about the discriminator.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUndiscriminatedOneOfComponentBecomesAChoiceType() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Card:
                  type: object
                  properties:
                    number: { type: string }
                Cash:
                  type: object
                  properties:
                    amount: { type: number }
                PaymentMethod:
                  oneOf:
                    - $ref: '#/components/schemas/Card'
                    - $ref: '#/components/schemas/Cash'
                Order:
                  type: object
                  properties:
                    method:
                      $ref: '#/components/schemas/PaymentMethod'
            """);

        var paymentMethod = model.Schemas.First(s => s.Name == "PaymentMethod");

        Assert.Equal(SchemaKind.OneOf, paymentMethod.Kind);

        // Named by the component, so both branches are reachable and neither renames the type.
        Assert.Equal(
            new[] { "#/components/schemas/Card", "#/components/schemas/Cash" },
            paymentMethod.OneOf.Select(branch => branch.Ref).ToArray());

        var method = model.Schemas.First(s => s.Name == "Order").Properties.First(p => p.Name == "method");

        // The property carries the reference it was written with, and types as the choice.
        Assert.Equal("#/components/schemas/PaymentMethod", method.Ref);
        Assert.Equal("PaymentMethod", TypeMapper.MapPropertyToCSharpType(method));
    }

    // ── nested and inline schemas ─────────────────────────────────────────────────────────────

    /// <summary>
    /// An inline enum on a property keeps its values but gets no generated enum type — the property
    /// stays a string, because the enum has no name to declare a type under.
    /// </summary>
    [Fact]
    public void AnInlineEnumStaysAStringWithItsValuesRecorded() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Pet:
                  type: object
                  properties:
                    status:
                      type: string
                      enum: [available, pending, sold]
            """);

        var status = model.Schemas.First(s => s.Name == "Pet").Properties.First(p => p.Name == "status");

        Assert.Equal(["available", "pending", "sold"], status.EnumValues);
        Assert.Equal("string", TypeMapper.MapPropertyToCSharpType(status));
    }

    /// <summary>
    /// A named enum schema becomes an enum type of its own, and the values keep the order the
    /// document gave them.
    /// </summary>
    [Fact]
    public void ANamedEnumSchemaBecomesAnEnumKind() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                PetStatus:
                  type: string
                  enum: [available, pending, sold]
            """);

        var status = model.Schemas.First(s => s.Name == "PetStatus");

        Assert.Equal(SchemaKind.Enum, status.Kind);
        Assert.Equal(["available", "pending", "sold"], status.EnumValues);
    }

    /// <summary>
    /// An object nested inline inside a property is lifted into a schema of its own, named for
    /// where it sits. It used to map to <c>JsonElement</c> for want of a name.
    /// </summary>
    [Fact]
    public void AnInlineNestedObjectIsLiftedIntoASchema() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Order:
                  type: object
                  properties:
                    shipping:
                      type: object
                      properties:
                        street: { type: string }
            """);

        var shipping = model.Schemas.First(s => s.Name == "Order").Properties.First(p => p.Name == "shipping");

        Assert.Equal("OrderShipping", TypeMapper.MapPropertyToCSharpType(shipping));
        Assert.Contains(model.Schemas, schema => schema.Name == "OrderShipping");
    }

    /// <summary>
    /// <c>additionalProperties</c> with no declared properties makes the schema a dictionary rather
    /// than an object with none.
    /// </summary>
    [Fact]
    public void AdditionalPropertiesAloneMakesTheSchemaADictionary() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Part:
                  type: object
                  properties:
                    sku: { type: string }
                PartsByName:
                  type: object
                  additionalProperties:
                    $ref: '#/components/schemas/Part'
            """);

        var dictionary = model.Schemas.First(s => s.Name == "PartsByName");

        Assert.Equal(SchemaKind.Dictionary, dictionary.Kind);
        Assert.Equal("#/components/schemas/Part", dictionary.DictionaryValueRef);
    }

    /// <summary>
    /// A dictionary-valued property is recognised on the property rather than only on a schema of its
    /// own, and maps to <c>Dictionary&lt;string, T&gt;</c>.
    /// </summary>
    [Fact]
    public void ADictionaryValuedPropertyMapsToADictionary() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths: {}
            components:
              schemas:
                Widget:
                  type: object
                  properties:
                    labels:
                      type: object
                      additionalProperties: { type: string }
            """);

        var labels = model.Schemas.First(s => s.Name == "Widget").Properties.First(p => p.Name == "labels");

        Assert.True(labels.IsDictionary);
        Assert.Equal("Dictionary<string, string>", TypeMapper.MapPropertyToCSharpType(labels));
    }

    // ── optionality ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The <c>required</c> list is what decides optionality. A property named there is required; one
    /// left out of it is optional, and the record emitter gives it a nullable type and a default.
    /// </summary>
    [Fact]
    public void OmissionFromTheRequiredListIsWhatMakesAPropertyOptional() {
        var model = Parse(
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
                    tag: { type: string }
            """);

        var pet = model.Schemas.First(s => s.Name == "Pet");

        Assert.True(pet.Properties.First(p => p.Name == "id").IsRequired);
        Assert.False(pet.Properties.First(p => p.Name == "tag").IsRequired);
        Assert.Equal(["id"], pet.Required);
    }

    /// <summary>
    /// A schema with no <c>required</c> list at all: every property is optional, and the list is
    /// empty rather than null so the emitters do not have to guard it.
    /// </summary>
    [Fact]
    public void ASchemaWithNoRequiredListHasNoRequiredProperties() {
        var model = Parse(
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
                    tag: { type: string }
            """);

        var pet = model.Schemas.First(s => s.Name == "Pet");

        Assert.Empty(pet.Required);
        Assert.All(pet.Properties, property => Assert.False(property.IsRequired));
    }

    // ── parameters ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every <c>in</c> location an OpenAPI 3 parameter can carry, lowercased. The generator decides
    /// how to bind a parameter from this string, so a casing change silently turns a path token into
    /// a query string read.
    /// </summary>
    [Theory]
    [InlineData("path", "path")]
    [InlineData("query", "query")]
    [InlineData("header", "header")]
    [InlineData("cookie", "cookie")]
    public void EveryParameterLocationIsRecordedLowercased(string location, string expected) {
        var model = Parse(
            $$"""
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /things/{thingId}:
                get:
                  tags: [Thing]
                  operationId: getThing
                  parameters:
                    - name: thingId
                      in: path
                      required: true
                      schema: { type: string }
                    - name: probe
                      in: {{location}}
                      required: true
                      schema: { type: string }
                  responses:
                    '200': { description: ok }
            """);

        var operation = model.Services.First(s => s.Tag == "Thing").Operations.Single();
        var probe = operation.Parameters.First(p => p.Name == "probe");

        Assert.Equal(expected, probe.In);
        Assert.True(probe.IsRequired);
    }

    /// <summary>
    /// A parameter with no <c>required</c> flag defaults to optional, which is what makes the
    /// generated parameter nullable.
    /// </summary>
    [Fact]
    public void AParameterWithoutARequiredFlagIsOptional() {
        var model = Parse(
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
                      schema: { type: integer }
                  responses:
                    '200': { description: ok }
            """);

        var page = model.Services.First(s => s.Tag == "Thing").Operations.Single().Parameters.Single();

        Assert.False(page.IsRequired);
    }

    /// <summary>
    /// An array-typed query parameter keeps its item type, so the generated parameter is a
    /// <c>List&lt;T&gt;</c> rather than a single value.
    /// </summary>
    [Fact]
    public void AnArrayParameterKeepsItsItemType() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /things:
                get:
                  tags: [Thing]
                  operationId: listThings
                  parameters:
                    - name: ids
                      in: query
                      schema:
                        type: array
                        items: { type: integer }
                  responses:
                    '200': { description: ok }
            """);

        var ids = model.Services.First(s => s.Tag == "Thing").Operations.Single().Parameters.Single();

        Assert.True(ids.IsArray);
        Assert.Equal("integer", ids.ArrayItemsType);
        Assert.Equal("List<int>", TypeMapper.MapParameterToCSharpType(ids));
    }

    /// <summary>
    /// <c>x-codegen-exclude</c> drops a parameter before it reaches the model, so it never appears in
    /// a generated signature.
    /// </summary>
    [Fact]
    public void AParameterMarkedCodegenExcludeIsDropped() {
        var model = Parse(Specs.CodegenExcludedParameter);

        var parameters = model.Services.First(s => s.Tag == "Thing").Operations.Single().Parameters;

        Assert.Equal("page", Assert.Single(parameters).Name);
    }

    /// <summary>
    /// A parameter declared once on the path item reaches every operation on that path.
    /// </summary>
    /// <remarks>
    /// Writing <c>{petId}</c> once rather than on each of GET, PUT, PATCH and DELETE is the shape
    /// OpenAPI offers for exactly this, and it used to be discarded: the parser read only
    /// <c>operation.Parameters</c>, so the generated handlers had no <c>petId</c> to receive while
    /// the route still matched on its wildcard node.
    /// </remarks>
    [Fact]
    public void APathItemParameterReachesEveryOperationOnThatPath() {
        var model = Parse(Specs.PathItemLevelParameters);

        var operations = model.Services.First(s => s.Tag == "Pet").Operations;

        Assert.Equal(3, operations.Count);

        foreach (var operation in operations) {
            var petId = Assert.Single(operation.Parameters, p => p.Name == "petId" && p.In == "path");

            Assert.True(petId.IsRequired);
            Assert.Equal("string", petId.Type);
        }
    }

    /// <summary>
    /// A path-item parameter comes before the operation's own, and an operation's own is still
    /// its own. Order here is the generated method's signature order.
    /// </summary>
    [Fact]
    public void APathItemParameterPrecedesTheOperationsOwn() {
        var model = Parse(Specs.PathItemLevelParameters);

        var list = model.Services.First(s => s.Tag == "Pet")
            .Operations.First(o => o.OperationId == "listPetToys").Parameters;

        Assert.Equal(new[] { "petId", "limit" }, list.Select(p => p.Name).ToArray());
    }

    /// <summary>
    /// An operation redeclaring a path-item parameter replaces it, and does so in place rather
    /// than appending — so overriding a parameter does not reorder the signature.
    /// </summary>
    [Fact]
    public void AnOperationOverridesAPathItemParameterInPlace() {
        var model = Parse(Specs.PathItemLevelParameters);

        var replace = model.Services.First(s => s.Tag == "Pet")
            .Operations.First(o => o.OperationId == "replacePet").Parameters;

        Assert.Equal(new[] { "petId" }, replace.Select(p => p.Name).ToArray());

        // The operation's own declaration constrains what the shared one did not.
        Assert.Equal(36, Assert.Single(replace).MaxLength);
    }

    /// <summary>
    /// Name and location together identify a parameter, so the same name in two locations is two
    /// parameters and neither overrides the other.
    /// </summary>
    /// <remarks>
    /// Asserted at the parser only. The generated <c>Parameters</c> class names its members after
    /// the parameter alone, so a spec like this does not compile — but that collision is a naming
    /// question downstream of here, and it happens just the same when one operation declares both
    /// itself. Merging the two into one parameter would be the wrong answer to it.
    /// </remarks>
    [Fact]
    public void AParameterIsIdentifiedByNameAndLocationTogether() {
        var model = Parse(Specs.ParametersSharingANameAcrossLocations);

        var deletePet = model.Services.First(s => s.Tag == "Pet")
            .Operations.First(o => o.OperationId == "deletePet").Parameters;

        Assert.Equal(new[] { "petId", "petId" }, deletePet.Select(p => p.Name).ToArray());
        Assert.Equal(new[] { "path", "header" }, deletePet.Select(p => p.In).ToArray());
    }

    // ── polymorphism ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>oneOf</c> carrying a <c>discriminator</c> is the base of a hierarchy, so it becomes an
    /// object rather than falling through to the untyped primitive case.
    /// </summary>
    [Fact]
    public void ADiscriminatedOneOfBecomesAPolymorphicBase() {
        var model = Parse(Specs.DiscriminatedHierarchy);

        var pet = model.Schemas.First(s => s.Name == "Pet");

        Assert.Equal(SchemaKind.Object, pet.Kind);
        Assert.True(pet.IsPolymorphicBase);
        Assert.Equal("petType", pet.DiscriminatorPropertyName);
    }

    /// <summary>The mapping is read in document order, value to reference.</summary>
    [Fact]
    public void TheDiscriminatorMappingIsRead() {
        var model = Parse(Specs.DiscriminatedHierarchy);

        var pet = model.Schemas.First(s => s.Name == "Pet");

        Assert.Equal(new[] { "dog", "cat" }, pet.DiscriminatorMapping.Select(m => m.Value).ToArray());
        Assert.Equal(
            new[] { "#/components/schemas/Dog", "#/components/schemas/Cat" },
            pet.DiscriminatorMapping.Select(m => m.Ref).ToArray());
    }

    /// <summary>
    /// A schema composing a discriminated base with <c>allOf</c> records which base it derives
    /// from, and still carries the merged property set its record declares.
    /// </summary>
    [Fact]
    public void AnAllOfBranchPointingAtADiscriminatedBaseSetsTheBaseRef() {
        var model = Parse(Specs.DiscriminatedHierarchy);

        var dog = model.Schemas.First(s => s.Name == "Dog");

        Assert.Equal("#/components/schemas/Pet", dog.BaseRef);
        Assert.False(dog.IsPolymorphicBase);

        // The base's properties are merged in, which is what lets the record forward them.
        Assert.Contains(dog.Properties, p => p.Name == "petType");
        Assert.Contains(dog.Properties, p => p.Name == "breed");
    }

    /// <summary>
    /// An ordinary <c>allOf</c> composition, with no discriminator anywhere, is still flattened
    /// rather than turned into inheritance.
    /// </summary>
    [Fact]
    public void AnAllOfWithoutADiscriminatedBaseIsStillFlattened() {
        var model = Parse(Specs.AllOfComposition);

        var dog = model.Schemas.First(s => s.Name == "Dog");

        Assert.Null(dog.BaseRef);
        Assert.Contains(dog.Properties, p => p.Name == "id");
        Assert.Contains(dog.Properties, p => p.Name == "breed");
    }

    /// <summary>Every constraint the validation emitter reads survives parsing.</summary>
    [Fact]
    public void EveryValidationConstraintOnAParameterIsParsed() {
        var model = Parse(Specs.EveryValidationConstraint);

        var operation = model.Services.First(s => s.Tag == "Order")
            .Operations.First(o => o.OperationId == "listOrders");

        var limit = operation.Parameters.First(p => p.Name == "limit");

        Assert.Equal(1m, limit.Minimum);
        Assert.Equal(100m, limit.Maximum);

        var sort = operation.Parameters.First(p => p.Name == "sort");

        Assert.Equal(["asc", "desc"], sort.EnumValues);

        var skus = operation.Parameters.First(p => p.Name == "skus");

        Assert.Equal(1, skus.MinItems);
        Assert.Equal(10, skus.MaxItems);
    }

    /// <summary>
    /// Exclusive bounds are a separate flag rather than an adjusted number, because the emitted
    /// <c>RangeRule</c> takes them that way.
    /// </summary>
    [Fact]
    public void ExclusiveBoundsOnABodyPropertyAreParsedAsFlags() {
        var model = Parse(Specs.EveryValidationConstraint);

        var create = model.Services.First(s => s.Tag == "Order")
            .Operations.First(o => o.OperationId == "createOrder");

        var discount = create.RequestBodyProperties.First(p => p.Name == "discount");

        Assert.Equal(0m, discount.Minimum);
        Assert.Equal(1m, discount.Maximum);
        Assert.True(discount.ExclusiveMinimum);
        Assert.True(discount.ExclusiveMaximum);
    }

    /// <summary>
    /// The body's <c>required</c> list reaches the operation, which is what turns into a
    /// <c>RequiredRule</c> on the property rather than only a non-nullable record parameter.
    /// </summary>
    [Fact]
    public void ARequestBodysRequiredPropertiesReachTheOperation() {
        var model = Parse(Specs.EveryValidationConstraint);

        var create = model.Services.First(s => s.Tag == "Order")
            .Operations.First(o => o.OperationId == "createOrder");

        Assert.Equal(["sku"], create.RequestBodyRequired);
        Assert.True(create.RequestBodyProperties.First(p => p.Name == "sku").IsRequired);
        Assert.False(create.RequestBodyProperties.First(p => p.Name == "quantity").IsRequired);
    }

    /// <summary>
    /// An operation with nothing to validate produces no filter provider at all, so a route with no
    /// constraints does not pay for a filter that would pass everything.
    /// </summary>
    [Fact]
    public void AnOperationWithNoConstraintsHasNoValidationToDo() {
        var model = Parse(
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
                      schema: { type: integer }
                  responses:
                    '200': { description: ok }
            """);

        var operation = model.Services.First(s => s.Tag == "Thing").Operations.Single();

        Assert.False(operation.HasValidationConstraints);
    }

    // ── operations and responses ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Operations are grouped by their first tag, which is what decides the service interface each
    /// one lands on.
    /// </summary>
    [Fact]
    public void OperationsAreGroupedByTheirFirstTag() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /pets:
                get:
                  tags: [Pet, Inventory]
                  operationId: listPets
                  responses:
                    '200': { description: ok }
            """);

        Assert.Equal("Pet", Assert.Single(model.Services).Tag);
    }

    /// <summary>
    /// The success status comes from the lowest 2xx response the document declares, not from a
    /// hardcoded 200.
    /// </summary>
    [Fact]
    public void TheSuccessStatusComesFromTheLowest2xxResponse() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /pets:
                post:
                  tags: [Pet]
                  operationId: createPet
                  responses:
                    '400': { description: bad }
                    '201': { description: created }
                    '202': { description: accepted }
            """);

        Assert.Equal(201, model.Services.Single().Operations.Single().SuccessStatusCode);
    }

    /// <summary>
    /// An operation whose only responses are errors keeps the default success status rather than
    /// picking up a 4xx.
    /// </summary>
    [Fact]
    public void AnOperationWithNoSuccessResponseKeepsTheDefaultStatus() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /pets:
                get:
                  tags: [Pet]
                  operationId: listPets
                  responses:
                    '404': { description: gone }
            """);

        Assert.Equal(200, model.Services.Single().Operations.Single().SuccessStatusCode);
    }

    /// <summary>
    /// A non-JSON response contributes its schema and its media type.
    /// </summary>
    /// <remarks>
    /// This test previously asserted the opposite - that <c>text/plain</c> recorded no response type
    /// at all - and described that as a response body only counting when it is JSON. It was
    /// describing a defect rather than a decision. The parser filtered the content map down to
    /// entries whose key contained "json" and dropped the rest, so a spec could declare a plain-text
    /// endpoint and get a generated interface method returning bare <c>Task</c>, with the content
    /// type never reaching <c>RawResponseContentType</c> and the pipeline JSON-encoding the string
    /// it returned.
    /// </remarks>
    [Fact]
    public void ANonJsonResponseRecordsItsSchemaAndMediaType() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /robots.txt:
                get:
                  tags: [Meta]
                  operationId: robots
                  responses:
                    '200':
                      description: ok
                      content:
                        text/plain:
                          schema: { type: string }
            """);

        var operation = model.Services.Single().Operations.Single();

        Assert.Equal("string", operation.ResponseType);
        Assert.Equal("text/plain", operation.ResponseContentType);
    }

    /// <summary>
    /// An unknown extension is ignored rather than rejected, which is what lets a document carry
    /// vendor extensions Hardened has no opinion about.
    /// </summary>
    /// <remarks>
    /// <c>x-hardened-template</c> used to be read here. It is gone: which server-side view renders
    /// a response is not part of an HTTP contract - clients, gateways and documentation tooling
    /// have no views, and a second implementation of the same specification in another language has
    /// nothing to do with the value. It also created two sources of truth with a precedence order,
    /// with the specification's value acting as a default the implementation overrode.
    /// <c>[Template&lt;T&gt;]</c> on the implementation is where that is said now.
    /// </remarks>
    [Fact]
    public void AnUnknownVendorExtensionIsIgnored() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /fortunes:
                get:
                  tags: [Fortune]
                  operationId: fortunes
                  x-hardened-template: Fortunes
                  responses:
                    '200':
                      description: ok
                      content:
                        text/html:
                          schema: { type: string }
            """);

        var operation = model.Services.Single().Operations.Single();

        Assert.Equal("fortunes", operation.OperationId);
        Assert.Equal("text/html", operation.ResponseContentType);
    }

    /// <summary>
    /// JSON is preferred when an operation offers both, because that is what the pipeline
    /// serializes by default - taking the document's first entry instead would change what
    /// already-generated code returns for every operation that lists a non-JSON type first.
    /// </summary>
    [Fact]
    public void AnOperationOfferingJsonAndSomethingElseRecordsJson() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /pets:
                get:
                  tags: [Pet]
                  operationId: listPets
                  responses:
                    '200':
                      description: ok
                      content:
                        text/csv:
                          schema: { type: string }
                        application/json:
                          schema: { type: string }
            """);

        Assert.Equal("application/json",
            model.Services.Single().Operations.Single().ResponseContentType);
    }

    /// <summary>
    /// The request body's media type is recorded on the same basis as the response's.
    /// </summary>
    [Fact]
    public void ANonJsonRequestBodyRecordsItsMediaType() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /upload:
                post:
                  tags: [Upload]
                  operationId: upload
                  requestBody:
                    content:
                      text/plain:
                        schema: { type: string }
                  responses:
                    '200': { description: ok }
            """);

        Assert.Equal("text/plain",
            model.Services.Single().Operations.Single().RequestBodyContentType);
    }

    /// <summary>
    /// An operation that declares no response content records no media type, which is what keeps
    /// it on the JSON path rather than sending it through the raw writer with a null content type.
    /// </summary>
    [Fact]
    public void AnOperationWithNoResponseContentRecordsNoMediaType() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /ping:
                get:
                  tags: [Meta]
                  operationId: ping
                  responses:
                    '204': { description: no content }
            """);

        Assert.Null(model.Services.Single().Operations.Single().ResponseContentType);
    }

    /// <summary>An array response records the item reference rather than the array's own.</summary>
    [Fact]
    public void AnArrayResponseRecordsItsItemReference() {
        var model = Parse(Specs.EverySchemaShape);

        var operation = model.Services.Single().Operations.Single();

        Assert.True(operation.ResponseIsArray);
        Assert.Equal("#/components/schemas/Widget", operation.ResponseArrayItemsRef);
    }

    /// <summary>
    /// The same path under two verbs is two operations, not one — this is what lets a route table
    /// send <c>GET /pets</c> and <c>POST /pets</c> to different handlers.
    /// </summary>
    [Fact]
    public void OnePathUnderTwoVerbsIsTwoOperations() {
        var model = Parse(Specs.EveryVerb);

        var operations = model.Services.Single().Operations;

        Assert.Equal(6, operations.Count);
        Assert.Equal(
            ["DELETE", "GET", "GET", "PATCH", "POST", "PUT"],
            operations.Select(o => o.HttpMethod).OrderBy(m => m, StringComparer.Ordinal));
    }

    // ── document-level extensions ─────────────────────────────────────────────────────────────

    [Fact]
    public void FilterTypesAreParsedWithTheirDefaults() {
        var model = Parse(Specs.FilterTypes);

        var rateLimit = model.FilterTypes.First(f => f.Name == "RateLimit");

        Assert.Equal("TestNamespace.Filters", rateLimit.Namespace);
        Assert.Equal("RateLimitAttribute", rateLimit.ClassName);
        Assert.True(rateLimit.Generate);

        var maxRequests = rateLimit.Properties.First(p => p.Name == "MaxRequests");

        Assert.Equal("int", maxRequests.CSharpType);
        Assert.Equal("100", maxRequests.Default);
    }

    /// <summary>
    /// <c>generate: false</c> is how a spec points at a filter attribute that already exists in a
    /// referenced library. The entry is still parsed, because <c>x-filters</c> needs it to resolve
    /// the type name.
    /// </summary>
    [Fact]
    public void AFilterTypeCanDeclareThatItIsDefinedElsewhere() {
        var model = Parse(Specs.FilterTypes);

        Assert.False(model.FilterTypes.First(f => f.Name == "External").Generate);
    }

    /// <summary>
    /// A filter type with no namespace is dropped: without one there is nowhere to emit the
    /// attribute, and nothing to qualify the <c>x-filters</c> reference with.
    /// </summary>
    [Fact]
    public void AFilterTypeWithNoNamespaceIsDropped() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            x-filter-types:
              Nameless:
                generate: true
                properties:
                  Level: { type: string }
            paths: {}
            """);

        Assert.Empty(model.FilterTypes);
    }

    [Fact]
    public void FilterInstancesCarryTheirOverriddenPropertyValues() {
        var model = Parse(Specs.FilterTypes);

        var instance = Assert.Single(model.Services.Single().Operations.Single().FilterInstances);

        Assert.Equal("RateLimit", instance.FilterTypeName);
        Assert.Equal("10", instance.PropertyValues["MaxRequests"]);
        Assert.Equal("second", instance.PropertyValues["Window"]);
        Assert.Equal("false", instance.PropertyValues["Enabled"]);
    }

    // ── naming ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A missing <c>operationId</c> is derived from the verb and the path, tokens included as
    /// <c>By…</c>.
    /// </summary>
    /// <remarks>
    /// Tokens used to be dropped, which made sibling routes indistinguishable: DigitalOcean names
    /// no operations at all, and <c>DELETE /v2/droplets</c> and <c>DELETE /v2/droplets/{id}</c>
    /// both produced <c>deleteV2Droplets</c> - one interface method declared twice.
    /// </remarks>
    [Fact]
    public void AMissingOperationIdIsDerivedFromTheVerbAndPath() {
        var model = Parse(
            """
            openapi: "3.0.0"
            info: { title: T, version: "1.0" }
            paths:
              /shipping-labels/{id}/history:
                get:
                  tags: [Label]
                  responses:
                    '200': { description: ok }
            """);

        Assert.Equal("getShippingLabelsByIdHistory", model.Services.Single().Operations.Single().OperationId);
    }
}
