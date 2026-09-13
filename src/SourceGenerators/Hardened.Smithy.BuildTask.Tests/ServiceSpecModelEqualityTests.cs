using Hardened.Generation.Models;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// Every member of <see cref="ServiceSpecModel"/> participates in equality, scalars and lists
/// alike.
/// </summary>
/// <remarks>
/// This model is what the incremental provider carries downstream, so a member it does not compare
/// is a contract edit that recomputes nothing. Title, Version and Servers were each absent once:
/// a contract that renamed itself, or added the servers it is published at, produced a model that
/// compared equal to the previous one and a document that kept the old values. The lists are
/// compared by count and then element by element, so both a resize and an in-place edit are
/// asserted.
/// </remarks>
public class ServiceSpecModelEqualityTests
{
    private static ServiceSpecModel Model() =>
        new()
        {
            FileName = "pets.json",
            JsonTypeInfoResolverName = "PetsContext",
            PublishUrl = "/openapi.json",
            UiUrl = "/scalar",
            SourceUrl = "https://example.test/pets.json",
            UiEnvironments = "Development",
            Title = "Pets",
            Version = "1.0.0",
            InfoDescription = "the pet store",
            ContentNegotiation = "Strict",
            ErrorBodies = "Json",
            ResponseModel = SpecResponseModel.Response,
            Serializer = SpecSerializer.MessagePackKeyed,
            BindCancellationToken = true,
            Schemas = [new SchemaModel { Name = "Pet" }],
            Services = [new ServiceModel { Tag = "pets" }],
            FilterTypes = [new FilterTypeModel { Name = "audit", Namespace = "Pets" }],
            SecuritySchemes = [new SecuritySchemeModel { Name = "bearer", Json = "{}" }],
            Servers = [new ServerModel { Url = "https://example.test" }],
            ValidatedOperations =
            [
                new ValidatedOperationModel { OperationId = "listPets", InterfaceName = "IPets" },
            ],
        };

    public static TheoryData<string> Members =>
        new()
        {
            "ContentNegotiation",
            "ErrorBodies",
            "ResponseModel",
            "Serializer",
            "BindCancellationToken",
            "FileName",
            "JsonTypeInfoResolverName",
            "PublishUrl",
            "UiUrl",
            "SourceUrl",
            "UiEnvironments",
            "Title",
            "Version",
            "InfoDescription",
        };

    private static void Change(ServiceSpecModel model, string member)
    {
        switch (member)
        {
            case "ContentNegotiation":
                model.ContentNegotiation = "Lenient";
                break;
            case "ErrorBodies":
                model.ErrorBodies = "Negotiated";
                break;
            case "ResponseModel":
                model.ResponseModel = SpecResponseModel.Union;
                break;
            case "Serializer":
                model.Serializer = SpecSerializer.Json;
                break;
            case "BindCancellationToken":
                model.BindCancellationToken = false;
                break;
            case "FileName":
                model.FileName = "other.json";
                break;
            case "JsonTypeInfoResolverName":
                model.JsonTypeInfoResolverName = "OtherContext";
                break;
            case "PublishUrl":
                model.PublishUrl = "/other.json";
                break;
            case "UiUrl":
                model.UiUrl = "/other";
                break;
            case "SourceUrl":
                model.SourceUrl = "https://example.test/other.json";
                break;
            case "UiEnvironments":
                model.UiEnvironments = "Production";
                break;
            case "Title":
                model.Title = "Other";
                break;
            case "Version":
                model.Version = "2.0.0";
                break;
            case "InfoDescription":
                model.InfoDescription = "other";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(member), member, "no case");
        }
    }

    [Theory]
    [MemberData(nameof(Members))]
    public void ChangingAnyScalar_MakesTheModelUnequal(string member)
    {
        var mutated = Model();
        Change(mutated, member);

        Assert.False(Model().Equals(mutated), $"{member} is not compared by Equals");
    }

    [Fact]
    public void TwoModelsBuiltTheSameWay_AreEqual()
    {
        Assert.Equal(Model(), Model());
        Assert.Equal(Model().GetHashCode(), Model().GetHashCode());
    }

    [Fact]
    public void AModel_EqualsItself()
    {
        var model = Model();

        Assert.True(model.Equals(model));
    }

    [Fact]
    public void NullAndOtherTypes_AreNotEqual()
    {
        Assert.False(Model().Equals(null));
        Assert.False(Model().Equals((object?)"not a spec"));
        Assert.True(Model().Equals((object?)Model()));
    }

    /// <summary>A list that grew or shrank is a different contract.</summary>
    [Theory]
    [InlineData("Schemas")]
    [InlineData("Services")]
    [InlineData("FilterTypes")]
    [InlineData("SecuritySchemes")]
    [InlineData("Servers")]
    [InlineData("ValidatedOperations")]
    public void AddingToAnyList_MakesTheModelUnequal(string list)
    {
        var mutated = Model();

        switch (list)
        {
            case "Schemas":
                mutated.Schemas.Add(new SchemaModel { Name = "Owner" });
                break;
            case "Services":
                mutated.Services.Add(new ServiceModel { Tag = "owners" });
                break;
            case "FilterTypes":
                mutated.FilterTypes.Add(new FilterTypeModel { Name = "trace", Namespace = "Pets" });
                break;
            case "SecuritySchemes":
                mutated.SecuritySchemes.Add(
                    new SecuritySchemeModel { Name = "apiKey", Json = "{}" }
                );
                break;
            case "Servers":
                mutated.Servers.Add(new ServerModel { Url = "https://other.test" });
                break;
            case "ValidatedOperations":
                mutated.ValidatedOperations.Add(
                    new ValidatedOperationModel { OperationId = "getPet", InterfaceName = "IPets" }
                );
                break;
        }

        Assert.False(Model().Equals(mutated), $"{list} count is not compared");
    }

    /// <summary>
    /// A list of the same length whose contents changed. The count check passes, so this is what
    /// reaches the element loop.
    /// </summary>
    [Theory]
    [InlineData("Schemas")]
    [InlineData("Services")]
    [InlineData("FilterTypes")]
    [InlineData("SecuritySchemes")]
    [InlineData("Servers")]
    [InlineData("ValidatedOperations")]
    public void EditingAnElementInPlace_MakesTheModelUnequal(string list)
    {
        var mutated = Model();

        switch (list)
        {
            case "Schemas":
                mutated.Schemas[0].Name = "Owner";
                break;
            case "Services":
                mutated.Services[0].Tag = "owners";
                break;
            case "FilterTypes":
                mutated.FilterTypes[0].Name = "trace";
                break;
            case "SecuritySchemes":
                mutated.SecuritySchemes[0].Name = "apiKey";
                break;
            case "Servers":
                mutated.Servers[0].Url = "https://other.test";
                break;
            case "ValidatedOperations":
                mutated.ValidatedOperations[0].OperationId = "getPet";
                break;
        }

        Assert.False(Model().Equals(mutated), $"{list} elements are not compared");
    }
}
