using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Idl.Validation;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The parameter interface one operation gets, when anything about it is constrained.
/// </summary>
/// <remarks>
/// <para>
/// At <b>35% line coverage</b>. This is the seam between the build task and the source generator:
/// the task cannot name the handler's nested <c>Parameters</c> class, so it names an interface, puts
/// the constraints on it, and lets the generator make <c>Parameters</c> implement it.
/// </para>
/// <para>
/// The decision worth guarding is <b>returning null</b>. An interface is emitted only when something
/// is actually constrained, and the same question decides whether <c>[ValidateNested]</c> goes on the
/// body — a <c>[ValidateNested]</c> naming a validator the generator declined to emit is CS0234 in a
/// generated file. Both answers have to come from one place to stay in step, and this is it.
/// </para>
/// </remarks>
public class OperationParametersTests {

    private static PatternRegistry Patterns() =>
        new(EmitterHarness.RootNamespace + ".Validation", "petstore");

    private static ParameterModel Parameter(
        string name = "petId",
        string type = "string",
        bool required = false,
        int? minLength = null,
        int? maxLength = null,
        string? refName = null,
        string @in = "path",
        string? pattern = null,
        string? routeConstraint = null) =>
        new() {
            Name = name,
            In = @in,
            Type = type,
            IsRequired = required,
            MinLength = minLength,
            MaxLength = maxLength,
            Ref = refName,
            Pattern = pattern,
            RouteConstraint = routeConstraint
        };

    private static OperationModel Operation(params ParameterModel[] parameters) =>
        new() {
            OperationId = "getPet",
            MethodName = "GetPet",
            Path = "/pets/{petId}",
            HttpMethod = "GET",
            Parameters = new List<ParameterModel>(parameters)
        };

    private static ServiceSpecModel Spec(OperationModel operation, params SchemaModel[] schemas) =>
        new() {
            Services = [new ServiceModel { Tag = "pets", Operations = [operation] }],
            Schemas = new List<SchemaModel>(schemas)
        };

    private static OperationParameters.Model? Build(
        OperationModel operation, params SchemaModel[] schemas) =>
        OperationParameters.Build(
            operation, Spec(operation, schemas), EmitterHarness.ModelsNamespace, Patterns());

    #region whether an interface is emitted at all

    /// <summary>
    /// Nothing constrained means no interface. Emitting an empty one would make the generator
    /// produce a validator with nothing to check.
    /// </summary>
    [Fact]
    public void AnOperationWithNoConstraintsGetsNoInterface() {
        Assert.Null(Build(Operation(Parameter())));
    }

    [Fact]
    public void AnOperationWithNoParametersAtAllGetsNoInterface() {
        Assert.Null(Build(Operation()));
    }

    [Fact]
    public void OneConstrainedParameterIsEnough() {
        Assert.NotNull(Build(Operation(Parameter(minLength: 1))));
    }

    #endregion

    #region shape

    [Fact]
    public void TheInterfaceIsNamedForTheOperationsMethodName() {
        Assert.Equal("IGetPetParameters", Build(Operation(Parameter(minLength: 1)))!.InterfaceName);
    }

    /// <summary>
    /// The document's own operation id is carried through unchanged — it is what a reader matches
    /// against, and it is not always a legal C# name.
    /// </summary>
    [Fact]
    public void TheDocumentsOperationIdIsCarried() {
        Assert.Equal("getPet", Build(Operation(Parameter(minLength: 1)))!.OperationId);
    }

    /// <summary>
    /// Every parameter becomes a member, not only the constrained ones — the interface has to match
    /// what the handler's <c>Parameters</c> class declares.
    /// </summary>
    [Fact]
    public void EveryParameterBecomesAMember() {
        var model = Build(Operation(
            Parameter("petId", minLength: 1),
            Parameter("name")));

        Assert.Equal(["petId", "name"], model!.Members.Select(member => member.Name));
    }

    [Fact]
    public void OnlyTheConstrainedParameterCarriesAttributes() {
        var model = Build(Operation(
            Parameter("petId", minLength: 1),
            Parameter("name")));

        Assert.NotEmpty(model!.Members[0].Attributes);
        Assert.Empty(model.Members[1].Attributes);
    }

    #endregion

    #region requiredness

    [Fact]
    public void ARequiredStringParameterCarriesRequired() {
        var model = Build(Operation(Parameter(@in: "query", required: true, minLength: 1)));

        Assert.Contains(
            model!.Members[0].Attributes, attribute => attribute.Type.Name == "RequiredAttribute");
    }

    /// <summary>
    /// Suppressed on a path parameter whatever its type: a request that reached the handler matched
    /// the template, and a segment that is absent or empty matched nothing and was answered 404. So
    /// <c>[Required]</c> there is a check that cannot fail, and an operation constrained only that
    /// way used to get a validator - and a 400 in its document that no caller could provoke.
    /// </summary>
    [Fact]
    public void ARequiredPathParameterDoesNotCarryRequired() {
        Assert.Null(Build(Operation(Parameter(required: true))));
    }

    /// <summary>
    /// Suppressed where the C# type already guarantees presence. A required integer path parameter
    /// is the common case, and <c>[Required]</c> on one makes the validation generator emit
    /// <c>value.petId is null</c> against a <c>long</c> — CS0037.
    /// </summary>
    [Fact]
    public void ARequiredNonNullableValueTypeDoesNotCarryRequired() {
        // A second, constrained parameter so an interface is produced at all — the integer's own
        // constraints are exactly what is expected to come back empty.
        var model = Build(Operation(
            Parameter("petId", type: "integer", required: true),
            Parameter("name", minLength: 1)));

        Assert.Equal("petId", model!.Members[0].Name);
        Assert.Empty(model.Members[0].Attributes);
    }

    /// <summary>
    /// A generated enum is a value type too, and the spec's own schemas are what say so.
    /// </summary>
    [Fact]
    public void ARequiredParameterTypedAsAGeneratedEnumDoesNotCarryRequired() {
        var operation = Operation(
            Parameter("status", required: true, refName: "#/components/schemas/PetStatus"),
            Parameter("name", minLength: 1));

        var model = OperationParameters.Build(
            operation,
            Spec(operation, new SchemaModel {
                Name = "PetStatus", Kind = SchemaKind.Enum, EnumValues = ["available", "sold"]
            }),
            EmitterHarness.ModelsNamespace,
            Patterns());

        Assert.NotNull(model);
        Assert.Equal("status", model!.Members[0].Name);
        Assert.Empty(model.Members[0].Attributes);
    }

    /// <summary>
    /// The suppression is about the type, not the name. The same parameter typed as a string does
    /// carry <c>[Required]</c>.
    /// </summary>
    [Fact]
    public void ARequiredStringParameterStillCarriesRequiredAlongsideAValueType() {
        var model = Build(Operation(
            Parameter("petId", type: "integer", required: true),
            Parameter("name", @in: "query", required: true, minLength: 1)));

        Assert.Empty(model!.Members[0].Attributes);
        Assert.Contains(
            model.Members[1].Attributes, attribute => attribute.Type.Name == "RequiredAttribute");
    }

    #endregion

    #region constraints the route already makes

    /// <summary>
    /// A pattern <c>RouteConstraintEmitter</c> compiled into the route is tested by the router,
    /// before binding, so the same test on the validation path can never fail. Emitted anyway it
    /// cost a regex match on every request that got through, and it published a 400 for an
    /// operation whose only declaration was that pattern.
    /// </summary>
    [Fact]
    public void APathPatternCompiledIntoTheRouteIsNotAlsoAValidatorCheck() {
        Assert.Null(Build(Operation(
            Parameter(pattern: "^[a-z]+$", routeConstraint: "spec_p_18b2c15a"))));
    }

    /// <summary>
    /// The pattern the registry refused compiles to no route constraint, so it stays where it was:
    /// on the validation path, which is the behaviour before route constraints existed.
    /// </summary>
    [Fact]
    public void APathPatternWithNoRouteConstraintIsStillAValidatorCheck() {
        var model = Build(Operation(Parameter(pattern: "^[a-z]+$")));

        Assert.Contains(
            model!.Members[0].Attributes, attribute => attribute.Type.Name == "PatternAttribute");
    }

    /// <summary>
    /// A query parameter's pattern is never a route constraint - it judges a request that did name
    /// a resource - so it is unaffected.
    /// </summary>
    [Fact]
    public void AQueryPatternIsAValidatorCheck() {
        var model = Build(Operation(Parameter(@in: "query", pattern: "^[a-z]+$")));

        Assert.Contains(
            model!.Members[0].Attributes, attribute => attribute.Type.Name == "PatternAttribute");
    }

    #endregion

    #region the request body

    private static SchemaModel Body(params PropertyModel[] properties) =>
        new() {
            Name = "Pet",
            Kind = SchemaKind.Object,
            Properties = new List<PropertyModel>(properties)
        };

    private static PropertyModel BodyProperty(
        string name = "name", string type = "string", int? minLength = null, bool readOnly = false) =>
        new() { Name = name, Type = type, MinLength = minLength, IsReadOnly = readOnly };

    private static OperationModel PostWithBody() =>
        new() {
            OperationId = "addPet",
            MethodName = "AddPet",
            Path = "/pets",
            HttpMethod = "POST",
            RequestBodyRef = "#/components/schemas/Pet"
        };

    [Fact]
    public void AConstrainedBodyAddsABodyMember() {
        var operation = PostWithBody();

        var model = OperationParameters.Build(
            operation,
            Spec(operation, Body(BodyProperty(minLength: 1))),
            EmitterHarness.ModelsNamespace,
            Patterns());

        Assert.NotNull(model);
        Assert.Equal("body", Assert.Single(model!.Members).Name);
    }

    /// <summary>
    /// <c>[ValidateNested]</c> is what makes the generated validator descend, which is what gives
    /// body errors their <c>body.</c> prefix and distinguishes them from a path parameter of the
    /// same name.
    /// </summary>
    [Fact]
    public void AConstrainedBodyCarriesValidateNested() {
        var operation = PostWithBody();

        var model = OperationParameters.Build(
            operation,
            Spec(operation, Body(BodyProperty(minLength: 1))),
            EmitterHarness.ModelsNamespace,
            Patterns());

        Assert.Equal(
            "ValidateNestedAttribute",
            Assert.Single(Assert.Single(model!.Members).Attributes).Type.Name);
    }

    /// <summary>
    /// A body with nothing to check gets no <c>[ValidateNested]</c>, and with no other constraint
    /// the operation gets no interface at all. Naming a validator the generator declined to emit is
    /// CS0234 in a generated file.
    /// </summary>
    [Fact]
    public void AnUnconstrainedBodyProducesNoInterface() {
        var operation = PostWithBody();

        Assert.Null(OperationParameters.Build(
            operation,
            Spec(operation, Body(BodyProperty())),
            EmitterHarness.ModelsNamespace,
            Patterns()));
    }

    /// <summary>
    /// A read-only property's constraints are never emitted, so a body whose only constrained
    /// property is read-only has nothing to descend into.
    /// </summary>
    [Fact]
    public void ABodyConstrainedOnlyOnAReadOnlyPropertyDoesNotValidateNested() {
        var operation = PostWithBody();

        Assert.Null(OperationParameters.Build(
            operation,
            Spec(operation, Body(BodyProperty(minLength: 1, readOnly: true))),
            EmitterHarness.ModelsNamespace,
            Patterns()));
    }

    [Fact]
    public void ABodyRefThatMatchesNoObjectSchemaAddsNoMember() {
        var operation = PostWithBody();

        Assert.Null(OperationParameters.Build(
            operation, Spec(operation), EmitterHarness.ModelsNamespace, Patterns()));
    }

    [Fact]
    public void AnOperationWithNoBodyRefAddsNoBodyMember() {
        var model = Build(Operation(Parameter(minLength: 1)));

        Assert.DoesNotContain(model!.Members, member => member.Name == "body");
    }

    /// <summary>
    /// A constrained parameter still produces an interface even when the body has nothing to check,
    /// and the body member comes along so the interface matches the Parameters class.
    /// </summary>
    [Fact]
    public void AConstrainedParameterCarriesAnUnconstrainedBodyAlong() {
        var operation = PostWithBody();

        operation.Parameters = [Parameter("petId", minLength: 1)];

        var model = OperationParameters.Build(
            operation,
            Spec(operation, Body(BodyProperty())),
            EmitterHarness.ModelsNamespace,
            Patterns());

        Assert.NotNull(model);
        Assert.Equal(["petId", "body"], model!.Members.Select(member => member.Name));
        Assert.Empty(model.Members[1].Attributes);
    }

    #endregion
}
