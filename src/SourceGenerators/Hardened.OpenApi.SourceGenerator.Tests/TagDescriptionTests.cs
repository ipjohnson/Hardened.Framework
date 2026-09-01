using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.Idl.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The description a contract's top-level tag declaration carries, through to the handler model
/// the document writer reads.
/// </summary>
public class TagDescriptionTests {

    [Fact]
    public void TheServiceDescriptionReachesEveryHandlerUnderTheTag() {
        var spec = new ServiceSpecModel {
            FileName = "pets",
            Services = new List<ServiceModel> {
                new() {
                    Tag = "Pet",
                    TagDescription = "Everything about pets.",
                    Operations = new List<OperationModel> {
                        new() {
                            OperationId = "listPets",
                            Tag = "Pet",
                            Path = "/pets",
                            HttpMethod = "GET",
                            SuccessStatusCode = 200,
                            SuccessResponses = { new SuccessResponseModel { StatusCode = 200 } }
                        }
                    }
                }
            }
        };

        var model = RequestModelBuilder
            .BuildModels(spec, "Test.Api.Models", "Test.Api.Services", "Test.Api.Generated",
                "Test.Api.Validation")
            .Single();

        Assert.Equal("Pet", model.Tag);
        Assert.Equal("Everything about pets.", model.TagDescription);
    }
}
