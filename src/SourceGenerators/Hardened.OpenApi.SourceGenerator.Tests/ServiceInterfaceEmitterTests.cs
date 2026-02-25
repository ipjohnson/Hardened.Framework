using Hardened.OpenApi.SourceGenerator.Emitters;
using Hardened.OpenApi.SourceGenerator.Models;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

public class ServiceInterfaceEmitterTests {
    [Fact]
    public void Emit_GeneratesInterfaceWithMethods() {
        var service = new ServiceModel {
            Tag = "Pet",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "listPets",
                    Path = "/pets",
                    HttpMethod = "GET",
                    Tag = "Pet",
                    SuccessStatusCode = 200,
                    ResponseRef = "#/components/schemas/PetList",
                    Parameters = new List<ParameterModel> {
                        new() {
                            Name = "limit",
                            In = "query",
                            IsRequired = false,
                            Type = "integer",
                            Format = "int32"
                        }
                    }
                },
                new() {
                    OperationId = "createPet",
                    Path = "/pets",
                    HttpMethod = "POST",
                    Tag = "Pet",
                    SuccessStatusCode = 201,
                    RequestBodyRef = "#/components/schemas/CreatePetRequest",
                    ResponseRef = "#/components/schemas/Pet"
                }
            }
        };

        var result = ServiceInterfaceEmitter.Emit(service, "Test.Api");

        Assert.Contains("namespace Test.Api.Services;", result);
        Assert.Contains("public interface IPetService", result);
        Assert.Contains("Task<PetList> ListPets(int? limit);", result);
        Assert.Contains("Task<Pet> CreatePet(CreatePetRequest body);", result);
    }

    [Fact]
    public void Emit_VoidReturn_GeneratesTask() {
        var service = new ServiceModel {
            Tag = "Pet",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "deletePet",
                    Path = "/pets/{petId}",
                    HttpMethod = "DELETE",
                    Tag = "Pet",
                    SuccessStatusCode = 204,
                    Parameters = new List<ParameterModel> {
                        new() { Name = "petId", In = "path", IsRequired = true, Type = "string" }
                    }
                }
            }
        };

        var result = ServiceInterfaceEmitter.Emit(service, "Test.Api");

        Assert.Contains("Task DeletePet(string petId);", result);
    }

    [Fact]
    public void Emit_ArrayReturnType_GeneratesTaskOfList() {
        var service = new ServiceModel {
            Tag = "Pet",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "listPets",
                    Path = "/pets",
                    HttpMethod = "GET",
                    Tag = "Pet",
                    SuccessStatusCode = 200,
                    ResponseIsArray = true,
                    ResponseArrayItemsRef = "#/components/schemas/Pet",
                    Parameters = new List<ParameterModel> {
                        new() {
                            Name = "limit",
                            In = "query",
                            IsRequired = false,
                            Type = "integer",
                            Format = "int32"
                        }
                    }
                }
            }
        };

        var result = ServiceInterfaceEmitter.Emit(service, "Test.Api");

        Assert.Contains("Task<List<Pet>> ListPets(int? limit);", result);
    }

    [Fact]
    public void Emit_HeaderParameters_ExcludedFromSignature() {
        var service = new ServiceModel {
            Tag = "Auth",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "getProfile",
                    Path = "/profile",
                    HttpMethod = "GET",
                    Tag = "Auth",
                    SuccessStatusCode = 200,
                    ResponseRef = "#/components/schemas/Profile",
                    Parameters = new List<ParameterModel> {
                        new() { Name = "Authorization", In = "header", IsRequired = true, Type = "string" },
                        new() { Name = "userId", In = "path", IsRequired = true, Type = "string" }
                    }
                }
            }
        };

        var result = ServiceInterfaceEmitter.Emit(service, "Test.Api");

        Assert.Contains("Task<Profile> GetProfile(string userId);", result);
        Assert.DoesNotContain("Authorization", result.Split('\n')
            .First(l => l.Contains("GetProfile")));
    }

    [Fact]
    public void Emit_SummaryXmlComments_GeneratedForMethods() {
        var service = new ServiceModel {
            Tag = "Pet",
            Operations = new List<OperationModel> {
                new() {
                    OperationId = "listPets",
                    Path = "/pets",
                    HttpMethod = "GET",
                    Tag = "Pet",
                    SuccessStatusCode = 200,
                    ResponseIsArray = true,
                    ResponseArrayItemsRef = "#/components/schemas/Pet"
                }
            }
        };

        var result = ServiceInterfaceEmitter.Emit(service, "Test.Api");

        Assert.Contains("/// <summary>GET /pets", result);
    }
}
