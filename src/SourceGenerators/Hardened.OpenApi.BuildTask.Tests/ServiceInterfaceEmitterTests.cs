using Hardened.Idl.Emitters;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

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

        var result = EmitterHarness.ServiceInterface(service);

        Assert.Contains("namespace Test.Api.Services\n{", result);
        Assert.Contains("public partial interface IPetService", result);
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

        var result = EmitterHarness.ServiceInterface(service);

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

        var result = EmitterHarness.ServiceInterface(service);

        Assert.Contains("Task<List<Pet>> ListPets(int? limit);", result);
    }

    /// <summary>
    /// A header parameter reaches the signature, so an implementation can read a value the
    /// framework already extracted.
    /// </summary>
    /// <remarks>
    /// It used to be excluded while the binder bound it, which left the value nowhere an
    /// implementation could see it. The three places that decide this - here, the binder, and the
    /// validation parameters interface - have to agree, or the generated <c>Parameters</c> class
    /// stops implementing its own interface.
    /// </remarks>
    [Fact]
    public void Emit_HeaderParameters_AppearInSignature() {
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

        var result = EmitterHarness.ServiceInterface(service);

        Assert.Contains("Task<Profile> GetProfile(string authorization, string userId);", result);
    }

    /// <summary>
    /// A cookie parameter reaches the signature too. Every location the specification allows is
    /// bound now, so the interface, the binder and the validation parameters interface take the
    /// same set.
    /// </summary>
    [Fact]
    public void Emit_CookieParameters_AppearInSignature() {
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
                        new() { Name = "session", In = "cookie", IsRequired = true, Type = "string" },
                        new() { Name = "userId", In = "path", IsRequired = true, Type = "string" }
                    }
                }
            }
        };

        var result = EmitterHarness.ServiceInterface(service);

        Assert.Contains("Task<Profile> GetProfile(string session, string userId);", result);
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

        var result = EmitterHarness.ServiceInterface(service);

        Assert.Contains("/// GET /pets", result);
    }
}
