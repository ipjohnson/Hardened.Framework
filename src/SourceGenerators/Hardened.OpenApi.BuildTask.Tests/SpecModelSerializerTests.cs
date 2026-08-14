using Hardened.OpenApi.SourceGenerator;
using Hardened.OpenApi.SourceGenerator.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The round trip between the build task and the source generator.
/// </summary>
/// <remarks>
/// The whole move rests on this. Everything downstream - records, enums, handlers, the routing table
/// - is generated from a model that has been through this serializer, so a field that does not
/// survive it produces wrong C# rather than an error. See <see cref="DeepEquality"/> for why the
/// models' own <c>Equals</c> is not enough to catch that.
/// </remarks>
public class SpecModelSerializerTests {

    [Fact]
    public void RoundTrip_PreservesEveryFieldOnAFullyPopulatedModel() {
        var model = FullyPopulated();

        DeepEquality.AssertEqual(model, SpecModelSerializer.Read(SpecModelSerializer.Write(model)));
    }

    [Fact]
    public void RoundTrip_PreservesAnEmptyModel() {
        var model = new OpenApiSpecModel { FileName = "empty" };

        DeepEquality.AssertEqual(model, SpecModelSerializer.Read(SpecModelSerializer.Write(model)));
    }

    /// <summary>
    /// The distinction the format is shaped around. A null <c>Type</c> and an empty one generate
    /// different C#, and a format that collapses them fails silently.
    /// </summary>
    [Fact]
    public void RoundTrip_KeepsNullDistinctFromEmptyString() {
        var model = new OpenApiSpecModel {
            FileName = "nulls",
            Schemas = {
                new SchemaModel {
                    Name = "Thing",
                    Kind = SchemaKind.Object,
                    Type = null,
                    Format = "",
                    Properties = {
                        new PropertyModel { Name = "a", Type = null, Format = "", Pattern = null },
                        new PropertyModel { Name = "b", Type = "", Format = null, Pattern = "" },
                    },
                },
            },
        };

        var result = SpecModelSerializer.Read(SpecModelSerializer.Write(model));
        var schema = result.Schemas.Single();

        Assert.Null(schema.Type);
        Assert.Equal("", schema.Format);
        Assert.Null(schema.Properties[0].Type);
        Assert.Equal("", schema.Properties[0].Format);
        Assert.Equal("", schema.Properties[1].Type);
        Assert.Null(schema.Properties[1].Format);
    }

    /// <summary>
    /// A null list and an empty one are different too: "declares no enum values" is not the same as
    /// "declares an empty set", and the emitters branch on it.
    /// </summary>
    [Fact]
    public void RoundTrip_KeepsANullListDistinctFromAnEmptyOne() {
        var model = new OpenApiSpecModel {
            FileName = "lists",
            Schemas = {
                new SchemaModel {
                    Name = "Thing",
                    Properties = {
                        new PropertyModel { Name = "none", EnumValues = null },
                        new PropertyModel { Name = "empty", EnumValues = new List<string>() },
                        new PropertyModel { Name = "some", EnumValues = new List<string> { "a", "b" } },
                    },
                },
            },
        };

        var properties = SpecModelSerializer.Read(SpecModelSerializer.Write(model)).Schemas.Single().Properties;

        Assert.Null(properties[0].EnumValues);
        Assert.NotNull(properties[1].EnumValues);
        Assert.Empty(properties[1].EnumValues!);
        Assert.Equal(new[] { "a", "b" }, properties[2].EnumValues);
    }

    /// <summary>
    /// Values arrive from yaml, so they carry whatever the author wrote. Tabs and newlines would
    /// otherwise split a record or a field and produce a model that reads back as a different shape.
    /// </summary>
    [Theory]
    [InlineData("with\ttab")]
    [InlineData("with\nnewline")]
    [InlineData("with\r\ncrlf")]
    [InlineData("with\\backslash")]
    [InlineData("with\\ttext that looks escaped")]
    [InlineData("with=equals")]
    [InlineData("withlist separator")]
    [InlineData("^[A-Z]{3}\\d+$")]
    [InlineData("")]
    public void RoundTrip_SurvivesAwkwardCharacters(string value) {
        var model = new OpenApiSpecModel {
            FileName = "escapes",
            Schemas = {
                new SchemaModel {
                    Name = "Thing",
                    Properties = { new PropertyModel { Name = "a", Pattern = value, EnumValues = new List<string> { value, "plain" } } },
                },
            },
        };

        var property = SpecModelSerializer.Read(SpecModelSerializer.Write(model)).Schemas.Single().Properties.Single();

        Assert.Equal(value, property.Pattern);
        Assert.Equal(new[] { value, "plain" }, property.EnumValues);
    }

    /// <summary>
    /// Generate defaults to true, so a false that is dropped reads back as true and starts emitting
    /// a type the spec asked us not to.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesGenerateFalseOnAFilterType() {
        var model = new OpenApiSpecModel {
            FileName = "filters",
            FilterTypes = {
                new FilterTypeModel { Name = "external", Namespace = "Some.Ns", Generate = false },
                new FilterTypeModel { Name = "ours", Namespace = "Some.Ns", Generate = true },
            },
        };

        var filterTypes = SpecModelSerializer.Read(SpecModelSerializer.Write(model)).FilterTypes;

        Assert.False(filterTypes[0].Generate);
        Assert.True(filterTypes[1].Generate);
    }

    /// <summary>
    /// Written output has to be byte-identical for identical input, or the task rewrites the model
    /// on every build, the timestamp moves, and the generator re-runs against an unchanged spec.
    /// </summary>
    [Fact]
    public void Write_IsStableAcrossCalls() {
        var model = FullyPopulated();

        Assert.Equal(SpecModelSerializer.Write(model), SpecModelSerializer.Write(model));
    }

    /// <summary>
    /// Filter property values live in a Dictionary, whose enumeration order is not guaranteed across
    /// runs. Unordered output would make every build look dirty to the Inputs/Outputs check.
    /// </summary>
    [Fact]
    public void Write_OrdersFilterPropertyValues() {
        var operation = new OperationModel { OperationId = "op" };
        var instance = new FilterInstanceModel { FilterTypeName = "f" };
        instance.PropertyValues["zebra"] = "1";
        instance.PropertyValues["apple"] = "2";
        instance.PropertyValues["mango"] = "3";
        operation.FilterInstances.Add(instance);

        var model = new OpenApiSpecModel {
            FileName = "s",
            Services = { new ServiceModel { Tag = "t", Operations = { operation } } },
        };

        var text = SpecModelSerializer.Write(model);

        Assert.True(text.IndexOf("apple", StringComparison.Ordinal) < text.IndexOf("mango", StringComparison.Ordinal));
        Assert.True(text.IndexOf("mango", StringComparison.Ordinal) < text.IndexOf("zebra", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no header at all")]
    [InlineData("#hardened-openapi-model 99\nspec\tFileName=x\n")]
    [InlineData("#hardened-openapi-model 1\nnosuchrecord\tKey=v\n")]
    [InlineData("#hardened-openapi-model 1\nspec\tmissing-the-equals\n")]
    public void Read_RejectsAnythingItDoesNotUnderstand(string text) {
        Assert.Throws<FormatException>(() => SpecModelSerializer.Read(text));
    }

    /// <summary>
    /// The round-trip test is only worth anything if the comparison can fail. A reflective comparer
    /// that quietly walks past a difference - because a type was treated as a leaf, or a collection
    /// was never enumerated - would make every assertion above vacuous.
    /// </summary>
    [Theory]
    [MemberData(nameof(SingleFieldMutations))]
    public void DeepEquality_DetectsADifferenceAtEveryDepth(string because, int mutation) {
        var expected = FullyPopulated();
        var actual = FullyPopulated();

        Mutations[mutation](actual);

        var failure = Assert.ThrowsAny<Exception>(() => DeepEquality.AssertEqual(expected, actual));

        Assert.Contains("Round trip lost or changed data", failure.Message);
        _ = because;
    }

    // Indexed rather than passed as delegates: the models are internal to the task assembly and
    // visible here only through InternalsVisibleTo, so a public [MemberData] cannot name them.
    public static TheoryData<string, int> SingleFieldMutations() {
        var data = new TheoryData<string, int>();

        for (var i = 0; i < MutationNames.Length; i++) {
            data.Add(MutationNames[i], i);
        }

        return data;
    }

    private static readonly string[] MutationNames = [
        "top-level scalar", "nested scalar", "null becoming a value", "value becoming null",
        "empty string vs null", "nullable int", "nullable decimal", "bool", "enum",
        "string list item", "string list length", "list becoming null", "deeply nested scalar",
        "dictionary value", "dictionary key", "collection length",
    ];

    private static readonly Action<OpenApiSpecModel>[] Mutations = [
        model => model.FileName = "changed",
        model => model.Schemas[0].Format = "changed",
        model => model.Schemas[1].Type = "object",
        model => model.Schemas[0].Type = null,
        model => model.Schemas[0].Format = "",
        model => model.Schemas[0].Properties[0].MinLength = 2,
        model => model.Schemas[0].Properties[0].Minimum = -1.4m,
        model => model.Schemas[0].Properties[0].ExclusiveMinimum = false,
        model => model.Schemas[1].Kind = SchemaKind.Object,
        model => model.Schemas[0].Required[0] = "changed",
        model => model.Schemas[0].Required.Clear(),
        model => model.Schemas[0].Properties[0].EnumValues = null,
        model => model.Services[0].Operations[0].Parameters[0].Pattern = "changed",
        model => model.Services[0].Operations[0].FilterInstances[0].PropertyValues["window"] = "61",
        model => model.Services[0].Operations[0].FilterInstances[0].PropertyValues.Remove("window"),
        model => model.Services[0].Operations.Clear(),
    ];

    /// <summary>
    /// Every field on every model set to a value distinguishable from its default, so the reflective
    /// comparison has something to catch when one goes missing.
    /// </summary>
    private static OpenApiSpecModel FullyPopulated() {
        var property = new PropertyModel {
            Name = "prop",
            Description = "What the property means.",
            Type = "string",
            Format = "date-time",
            Ref = "#/components/schemas/Other",
            IsArray = true,
            ArrayItemsRef = "#/components/schemas/Item",
            ArrayItemsType = "integer",
            ArrayItemsFormat = "int64",
            IsRequired = true,
            IsNullable = true,
            Default = "fallback",
            IsDictionary = true,
            DictionaryValueType = "string",
            DictionaryValueRef = "#/components/schemas/Value",
            EnumValues = new List<string> { "one", "two" },
            MinLength = 1,
            MaxLength = 99,
            Minimum = -1.5m,
            Maximum = 1000.25m,
            ExclusiveMinimum = true,
            ExclusiveMaximum = true,
            Pattern = "^[A-Z]{3}$",
            MinItems = 2,
            MaxItems = 20,
        };

        var parameter = new ParameterModel {
            Name = "petId",
            In = "path",
            Description = "The pet's identifier.",
            IsRequired = true,
            IsNullable = true,
            Default = "10",
            Type = "string",
            Format = "uuid",
            Ref = "#/components/parameters/PetId",
            IsArray = true,
            ArrayItemsType = "string",
            ArrayItemsRef = "#/components/schemas/Tag",
            EnumValues = new List<string> { "a", "b" },
            MinLength = 3,
            MaxLength = 36,
            Minimum = 0m,
            Maximum = 5m,
            ExclusiveMinimum = true,
            ExclusiveMaximum = true,
            Pattern = "^[a-z-]+$",
            MinItems = 1,
            MaxItems = 4,
        };

        var filterInstance = new FilterInstanceModel { FilterTypeName = "rateLimit" };
        filterInstance.PropertyValues["window"] = "60";
        filterInstance.PropertyValues["limit"] = "100";

        var operation = new OperationModel {
            OperationId = "getPet",
            Path = "/pets/{petId}",
            HttpMethod = "GET",
            Tag = "Pet",
            Description = "Returns a single pet.",
            Parameters = { parameter },
            RequestBodyContentType = "application/json",
            RequestBodyRef = "#/components/schemas/CreatePetRequest",
            RequestBodyType = "object",
            ResponseContentType = "text/plain",
            ResponseRef = "#/components/schemas/Pet",
            ResponseType = "object",
            ResponseFormat = "json",
            ResponseIsArray = true,
            ResponseArrayItemsRef = "#/components/schemas/Pet",
            SuccessStatusCode = 201,
            ErrorResponses = {
                new ErrorResponseModel {
                    StatusCode = 404, Ref = "#/components/schemas/ApiError", Description = "Gone."
                },
                new ErrorResponseModel { StatusCode = 503 },
            },
            TemplateName = "Fortunes",
            FilterInstances = { filterInstance },
            RequestBodyProperties = { property },
            RequestBodyRequired = { "name", "tag" },
        };

        return new OpenApiSpecModel {
            FileName = "petstore",
            Schemas = {
                new SchemaModel {
                    Name = "Pet",
                    Kind = SchemaKind.Object,
                    Description = "A pet in the store.",
                    DiscriminatorPropertyName = "petType",
                    BaseRef = "#/components/schemas/Animal",
                    DiscriminatorMapping = {
                        new DiscriminatorMappingModel { Value = "dog", Ref = "#/components/schemas/Dog" },
                        new DiscriminatorMappingModel { Value = "cat", Ref = "#/components/schemas/Cat" },
                    },
                    Properties = { property },
                    EnumValues = { "unused" },
                    Required = { "name" },
                    ArrayItemsRef = "#/components/schemas/Tag",
                    ArrayItemsType = "string",
                    ArrayItemsFormat = "uuid",
                    DictionaryValueType = "string",
                    DictionaryValueRef = "#/components/schemas/Value",
                    Type = "object",
                    Format = "custom",
                },
                new SchemaModel { Name = "PetStatus", Kind = SchemaKind.Enum, EnumValues = { "available", "sold" } },
            },
            Services = { new ServiceModel { Tag = "Pet", Operations = { operation } } },
            FilterTypes = {
                new FilterTypeModel {
                    Name = "rateLimit",
                    Namespace = "Sample.Filters",
                    Generate = false,
                    Properties = {
                        new FilterTypePropertyModel {
                            Name = "window",
                            CSharpType = "int",
                            Default = "60",
                            EnumType = "Sample.Window",
                            EnumValues = new List<string> { "short", "long" },
                        },
                    },
                },
            },
        };
    }
}
