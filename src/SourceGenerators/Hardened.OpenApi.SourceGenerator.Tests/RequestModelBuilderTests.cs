using CSharpAuthor;
using Hardened.OpenApi.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

public class RequestModelBuilderTests {
    private static OpenApiSpecModel CreatePetstoreSpec() {
        return new OpenApiSpecModel {
            FileName = "petstore",
            Schemas = new List<SchemaModel> {
                new() { Name = "Pet", Kind = SchemaKind.Object },
                new() { Name = "CreatePetRequest", Kind = SchemaKind.Object },
                new() { Name = "Store", Kind = SchemaKind.Object }
            },
            Services = new List<ServiceModel> {
                new() {
                    Tag = "Pet",
                    Operations = new List<OperationModel> {
                        new() {
                            OperationId = "listPets",
                            Path = "/pets",
                            HttpMethod = "GET",
                            ResponseIsArray = true,
                            ResponseArrayItemsRef = "#/components/schemas/Pet",
                            Parameters = new List<ParameterModel> {
                                new() { Name = "limit", In = "query", IsRequired = false, Type = "integer", Format = "int32" }
                            }
                        },
                        new() {
                            OperationId = "createPet",
                            Path = "/pets",
                            HttpMethod = "POST",
                            RequestBodyRef = "#/components/schemas/CreatePetRequest",
                            ResponseRef = "#/components/schemas/Pet"
                        },
                        new() {
                            OperationId = "getPet",
                            Path = "/pets/{petId}",
                            HttpMethod = "GET",
                            ResponseRef = "#/components/schemas/Pet",
                            Parameters = new List<ParameterModel> {
                                new() { Name = "petId", In = "path", IsRequired = true, Type = "string" }
                            }
                        },
                        new() {
                            OperationId = "deletePet",
                            Path = "/pets/{petId}",
                            HttpMethod = "DELETE",
                            SuccessStatusCode = 204,
                            Parameters = new List<ParameterModel> {
                                new() { Name = "petId", In = "path", IsRequired = true, Type = "string" }
                            }
                        }
                    }
                },
                new() {
                    Tag = "Store",
                    Operations = new List<OperationModel> {
                        new() {
                            OperationId = "listStores",
                            Path = "/stores",
                            HttpMethod = "GET",
                            ResponseIsArray = true,
                            ResponseArrayItemsRef = "#/components/schemas/Store"
                        }
                    }
                }
            }
        };
    }

    [Fact]
    public void BuildModels_PetstoreSpec_ProducesCorrectCountAndControllerTypes() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        Assert.Equal(5, models.Count);

        // All Pet operations should map to IPetService
        var petModels = models.Where(m => m.ControllerType.Name == "IPetService").ToList();
        Assert.Equal(4, petModels.Count);

        // Store operation should map to IStoreService
        var storeModels = models.Where(m => m.ControllerType.Name == "IStoreService").ToList();
        Assert.Single(storeModels);
    }

    [Fact]
    public void BuildModels_PetstoreSpec_HasCorrectMethodNamesAndPaths() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        Assert.Contains(models, m => m.HandlerMethod == "ListPets" && m.Name.Path == "/pets" && m.Name.Method == "GET");
        Assert.Contains(models, m => m.HandlerMethod == "CreatePet" && m.Name.Path == "/pets" && m.Name.Method == "POST");
        Assert.Contains(models, m => m.HandlerMethod == "GetPet" && m.Name.Path == "/pets/{petId}" && m.Name.Method == "GET");
        Assert.Contains(models, m => m.HandlerMethod == "DeletePet" && m.Name.Path == "/pets/{petId}" && m.Name.Method == "DELETE");
        Assert.Contains(models, m => m.HandlerMethod == "ListStores" && m.Name.Path == "/stores" && m.Name.Method == "GET");
    }

    [Fact]
    public void BuildModels_PathParameter_MapsToPathBindType() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var getPet = models.First(m => m.HandlerMethod == "GetPet");
        var petIdParam = getPet.RequestParameterInformationList.First(p => p.Name == "petId");

        Assert.Equal(ParameterBindType.Path, petIdParam.BindingType);
        Assert.True(petIdParam.Required);
        Assert.Equal("petId", petIdParam.BindingName);
    }

    [Fact]
    public void BuildModels_QueryParameter_MapsToQueryStringBindType() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var listPets = models.First(m => m.HandlerMethod == "ListPets");
        var limitParam = listPets.RequestParameterInformationList.First(p => p.Name == "limit");

        Assert.Equal(ParameterBindType.QueryString, limitParam.BindingType);
        Assert.False(limitParam.Required);
    }

    [Fact]
    public void BuildModels_RequestBody_MapsToBodyBindType() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var createPet = models.First(m => m.HandlerMethod == "CreatePet");
        var bodyParam = createPet.RequestParameterInformationList.First(p => p.Name == "body");

        Assert.Equal(ParameterBindType.Body, bodyParam.BindingType);
        Assert.True(bodyParam.Required);
    }

    [Fact]
    public void BuildModels_RefResponse_HasTaskOfTReturnType() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var getPet = models.First(m => m.HandlerMethod == "GetPet");
        Assert.NotNull(getPet.ResponseInformation.ReturnType);
        Assert.True(getPet.ResponseInformation.IsAsync);
    }

    [Fact]
    public void BuildModels_ArrayResponse_HasTaskOfListReturnType() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var listPets = models.First(m => m.HandlerMethod == "ListPets");
        Assert.NotNull(listPets.ResponseInformation.ReturnType);
        Assert.True(listPets.ResponseInformation.IsAsync);
    }

    [Fact]
    public void BuildModels_VoidResponse_HasNullReturnType() {
        var spec = CreatePetstoreSpec();

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var deletePet = models.First(m => m.HandlerMethod == "DeletePet");
        Assert.Null(deletePet.ResponseInformation.ReturnType);
        Assert.True(deletePet.ResponseInformation.IsAsync);
    }

    [Theory]
    [InlineData("IPetService", "PetController")]
    [InlineData("IService", "Controller")]
    [InlineData("PetHandler", "PetHandlerController")]
    [InlineData("IFoo", "FooController")]
    [InlineData("PetService", "PetController")]
    public void DeriveControllerName_VariousInputs_ProducesExpectedOutput(string interfaceName, string expected) {
        var result = RequestModelBuilder.DeriveControllerName(interfaceName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void EnrichWithHandlerFilters_NoHandlerInfos_ReturnsModelsUnchanged() {
        var spec = CreatePetstoreSpec();
        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var result = RequestModelBuilder.EnrichWithHandlerFilters(
            models, Array.Empty<HandlerInfo>());

        Assert.Same(models, result);
    }

    [Fact]
    public void EnrichWithHandlerFilters_ClassFilter_AppliedToAllMatchedHandlers() {
        var spec = CreatePetstoreSpec();
        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var classFilter = new AttributeModel(
            TypeDefinition.Get("Test", "MyFilterAttribute"), "", "");

        var handlerInfo = new HandlerInfo(
            TypeDefinition.Get("Test", "PetServiceImpl"),
            TypeDefinition.Get("Test.Api.Services", "IPetService"),
            new List<AttributeModel> { classFilter },
            new List<HandlerMethodFilterInfo>());

        var result = RequestModelBuilder.EnrichWithHandlerFilters(
            models, new List<HandlerInfo> { handlerInfo });

        // All Pet handlers should have the class filter
        var petModels = result.Where(m => m.ControllerType.Name == "IPetService").ToList();
        Assert.All(petModels, m => Assert.Contains(classFilter, m.Filters));

        // Store handler should not have the filter
        var storeModel = result.First(m => m.ControllerType.Name == "IStoreService");
        Assert.Empty(storeModel.Filters);
    }

    [Fact]
    public void EnrichWithHandlerFilters_MethodFilter_AppliedOnlyToMatchingMethod() {
        var spec = CreatePetstoreSpec();
        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var methodFilter = new AttributeModel(
            TypeDefinition.Get("Test", "AuthorizeAttribute"), "", "");

        var handlerInfo = new HandlerInfo(
            TypeDefinition.Get("Test", "PetServiceImpl"),
            TypeDefinition.Get("Test.Api.Services", "IPetService"),
            new List<AttributeModel>(),
            new List<HandlerMethodFilterInfo> {
                new("CreatePet", new List<AttributeModel> { methodFilter })
            });

        var result = RequestModelBuilder.EnrichWithHandlerFilters(
            models, new List<HandlerInfo> { handlerInfo });

        var createPet = result.First(m => m.HandlerMethod == "CreatePet");
        Assert.Contains(methodFilter, createPet.Filters);

        var listPets = result.First(m => m.HandlerMethod == "ListPets");
        Assert.Empty(listPets.Filters);
    }

    [Fact]
    public void BuildModels_HeaderParameter_MapsToHeaderBindType() {
        var spec = new OpenApiSpecModel {
            FileName = "test",
            Services = new List<ServiceModel> {
                new() {
                    Tag = "Auth",
                    Operations = new List<OperationModel> {
                        new() {
                            OperationId = "getProfile",
                            Path = "/profile",
                            HttpMethod = "GET",
                            Parameters = new List<ParameterModel> {
                                new() { Name = "Authorization", In = "header", IsRequired = true, Type = "string" }
                            }
                        }
                    }
                }
            }
        };

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Models", "Test.Services", "Test.Generated", "Test.Api.Validation");

        var getProfile = models.First(m => m.HandlerMethod == "GetProfile");
        var authParam = getProfile.RequestParameterInformationList.First(p => p.BindingName == "Authorization");

        Assert.Equal(ParameterBindType.Header, authParam.BindingType);
    }
}
