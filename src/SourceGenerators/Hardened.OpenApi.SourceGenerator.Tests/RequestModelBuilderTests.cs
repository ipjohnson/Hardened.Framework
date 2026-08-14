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

    private static RequestHandlerModel BuildTemplated(string? templateName, string? contentType) {
        var spec = SpecReturning(contentType);
        spec.Services[0].Operations[0].TemplateName = templateName;

        return RequestModelBuilder.BuildModels(
                spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation")
            .Single();
    }

    /// <summary>
    /// <c>x-hardened-template</c> reaches the response model, which is what
    /// <c>InvokeMethodCodeGenerator</c> turns into an assignment to
    /// <c>Response.TemplateName</c> for the engine to pick up.
    /// </summary>
    [Fact]
    public void BuildModels_TemplatedOperation_CarriesTheTemplateName() {
        Assert.Equal("Fortunes", BuildTemplated("Fortunes", "text/html").ResponseInformation.TemplateName);
    }

    /// <summary>
    /// A templated operation must not also take the raw path.
    /// <c>ContextSerializationService</c> checks <c>DefaultOutput</c> before the serializer locator,
    /// so a raw content type here would take the response first and hand the template's model to
    /// <c>RawOutputHelper</c>, which throws on anything that is not string, byte[] or Stream. The
    /// template would never render.
    /// </summary>
    [Fact]
    public void BuildModels_TemplatedOperation_DoesNotAlsoGetARawContentType() {
        Assert.True(string.IsNullOrEmpty(
            BuildTemplated("Fortunes", "text/html").ResponseInformation.RawResponseContentType));
    }

    /// <summary>
    /// Nor does a text/html declaration, templated or not. There is no longer a raw path for a spec
    /// to opt an operation into, which is why the clear-it-when-templated workaround that used to
    /// exist in two places is gone.
    /// </summary>
    [Fact]
    public void BuildModels_UntemplatedHtml_DoesNotForceTheContentTypeEither() {
        Assert.True(string.IsNullOrEmpty(
            BuildTemplated(null, "text/html").ResponseInformation.RawResponseContentType));
    }

    // ── [Template] on the implementation ───────────────────────────────

    private static AttributeModel TemplateAttribute(string name) =>
        new(TypeDefinition.Get("Hardened.Requests.Abstract.Attributes", "TemplateAttribute"),
            $"\"{name}\"", "");

    private static RequestHandlerModel EnrichRobots(
        string? specTemplate, string? contentType, params AttributeModel[] methodAttributes) {
        var spec = SpecReturning(contentType);
        spec.Services[0].Operations[0].TemplateName = specTemplate;

        var models = RequestModelBuilder.BuildModels(
            spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation");

        var handlerInfo = new HandlerInfo(
            TypeDefinition.Get("Test", "MetaServiceImpl"),
            TypeDefinition.Get("Test.Api.Services", "IMetaService"),
            new List<AttributeModel>(),
            new List<HandlerMethodFilterInfo> {
                new("Robots", methodAttributes.ToList())
            });

        return RequestModelBuilder.EnrichWithHandlerFilters(models, new List<HandlerInfo> { handlerInfo })
            .Single();
    }

    /// <summary>
    /// The view can be named on the implementation instead of in the spec. Which template renders a
    /// model is how the operation is fulfilled, not part of the contract it publishes.
    /// </summary>
    [Fact]
    public void EnrichWithHandlerFilters_TemplateAttribute_SetsTheTemplateName() {
        Assert.Equal("Fortunes",
            EnrichRobots(null, "text/html", TemplateAttribute("Fortunes")).ResponseInformation.TemplateName);
    }

    /// <summary>
    /// And it does not land in the filter list. Left there it would be emitted into the handler's
    /// metadata array as though it were an IExecutionFilter, and the name would reach nothing.
    /// </summary>
    [Fact]
    public void EnrichWithHandlerFilters_TemplateAttribute_IsNotTreatedAsAFilter() {
        Assert.Empty(EnrichRobots(null, "text/html", TemplateAttribute("Fortunes")).Filters);
    }

    /// <summary>Other method attributes are still filters.</summary>
    [Fact]
    public void EnrichWithHandlerFilters_TemplateAttribute_LeavesOtherAttributesAsFilters() {
        var other = new AttributeModel(TypeDefinition.Get("Test", "AuditAttribute"), "", "");

        var model = EnrichRobots(null, "text/html", TemplateAttribute("Fortunes"), other);

        Assert.Equal("Fortunes", model.ResponseInformation.TemplateName);
        Assert.Equal(new[] { other }, model.Filters);
    }

    /// <summary>
    /// A templated operation must not also carry a raw content type.
    /// <c>ContextSerializationService</c> checks <c>DefaultOutput</c> before any serializer, so the
    /// raw writer would take the response first and hand the template's model to
    /// <c>RawOutputHelper</c>, which throws on anything that is not string, byte[] or Stream. The
    /// spec here says text/html, which is exactly the case that produced that failure.
    /// </summary>
    [Fact]
    public void EnrichWithHandlerFilters_TemplateAttribute_ClearsTheRawContentType() {
        var model = EnrichRobots(null, "text/html", TemplateAttribute("Fortunes"));

        Assert.True(string.IsNullOrEmpty(model.ResponseInformation.RawResponseContentType));
    }

    /// <summary>
    /// The implementation overrides <c>x-hardened-template</c>. The spec's value is a default, and
    /// the implementation is the more specific statement about how the response is produced.
    /// </summary>
    [Fact]
    public void EnrichWithHandlerFilters_TemplateAttribute_OverridesTheSpecExtension() {
        Assert.Equal("FromAttribute",
            EnrichRobots("FromSpec", "text/html", TemplateAttribute("FromAttribute"))
                .ResponseInformation.TemplateName);
    }

    /// <summary>A spec-declared template still applies when the implementation says nothing.</summary>
    [Fact]
    public void EnrichWithHandlerFilters_NoTemplateAttribute_KeepsTheSpecExtension() {
        Assert.Equal("FromSpec", EnrichRobots("FromSpec", "text/html").ResponseInformation.TemplateName);
    }

    private static OpenApiSpecModel SpecReturning(string? contentType) =>
        new() {
            FileName = "content",
            Services = new List<ServiceModel> {
                new() {
                    Tag = "Meta",
                    Operations = new List<OperationModel> {
                        new() {
                            OperationId = "robots",
                            Path = "/robots.txt",
                            HttpMethod = "GET",
                            ResponseContentType = contentType,
                            ResponseType = "string"
                        }
                    }
                }
            }
        };

    private static RequestHandlerModel BuildRobots(string? contentType) =>
        RequestModelBuilder.BuildModels(
                SpecReturning(contentType),
                "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated", "Test.Api.Validation")
            .Single();

    /// <summary>
    /// A spec-declared media type does not force the response's content type.
    /// </summary>
    /// <remarks>
    /// It used to: the media type became <c>RawResponseContentType</c>, which the generator emitted
    /// as a <c>DefaultOutput</c> closure, so a <c>text/plain</c> operation answered text/plain
    /// whatever the client asked for. That is the producer overruling the client, which is not what
    /// a content map means - it describes the representation available, and the client says which
    /// it wants. <c>/plaintext</c> still answers text/plain because the client asks for it, and a
    /// handler that genuinely must force one says so with <c>[RawResponse]</c>.
    ///
    /// What the spec's media type still decides is the return type: reading the text/plain schema is
    /// what makes the generated method <c>Task&lt;string&gt;</c> rather than bare <c>Task</c>, and
    /// without a return value there is nothing to negotiate over at all.
    /// </remarks>
    [Fact]
    public void BuildModels_ANonJsonResponse_DoesNotForceTheContentType() {
        Assert.True(string.IsNullOrEmpty(BuildRobots("text/plain").ResponseInformation.RawResponseContentType));
    }

    /// <summary>
    /// JSON does not take the raw path. The raw writer writes the response value straight to the
    /// body, which is right for a string and wrong for a model.
    /// </summary>
    [Fact]
    public void BuildModels_JsonResponse_LeavesRawResponseContentTypeUnset() {
        Assert.True(string.IsNullOrEmpty(
            BuildRobots("application/json").ResponseInformation.RawResponseContentType));
    }

    /// <summary>An operation declaring no response content stays on the JSON path.</summary>
    [Fact]
    public void BuildModels_NoResponseContentType_LeavesRawResponseContentTypeUnset() {
        Assert.True(string.IsNullOrEmpty(BuildRobots(null).ResponseInformation.RawResponseContentType));
    }

    /// <summary>
    /// A vendor JSON media type is still JSON. Matching on the substring rather than on equality is
    /// what keeps application/problem+json and application/vnd.api+json off the raw path.
    /// </summary>
    [Fact]
    public void BuildModels_VendorJsonResponse_LeavesRawResponseContentTypeUnset() {
        Assert.True(string.IsNullOrEmpty(
            BuildRobots("application/problem+json").ResponseInformation.RawResponseContentType));
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
