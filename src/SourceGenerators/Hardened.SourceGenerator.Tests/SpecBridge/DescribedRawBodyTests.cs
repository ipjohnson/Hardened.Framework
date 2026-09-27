using System.Collections.Generic;
using System.Linq;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.SpecBridge;

/// <summary>
/// Whether a described scalar body is bound as the raw request body or read as JSON.
/// </summary>
/// <remarks>
/// The 0.41 trial's C-01: a Smithy <c>@httpPayload</c> blob was read as a base64 JSON string, so
/// raw bytes answered 400. The Smithy fixture sends the bytes end to end; these hold the rule for
/// both description languages.
/// </remarks>
public class DescribedRawBodyTests
{
    private static RequestParameterInformation Body(string type, string? format, string contentType)
    {
        var spec = new ServiceSpecModel
        {
            FileName = "fleet",
            ResponseModel = SpecResponseModel.Throws,
            Services = new List<ServiceModel>
            {
                new()
                {
                    Tag = "Van",
                    Operations = new List<OperationModel>
                    {
                        new()
                        {
                            OperationId = "uploadLog",
                            Path = "/vans/{vin}/logs",
                            HttpMethod = "POST",
                            SuccessStatusCode = 204,
                            SuccessResponses = { new SuccessResponseModel { StatusCode = 204 } },
                            RequestBodyType = type,
                            RequestBodyFormat = format,
                            RequestBodyContentType = contentType,
                            Parameters = new List<ParameterModel>
                            {
                                new()
                                {
                                    Name = "vin",
                                    In = "path",
                                    IsRequired = true,
                                    Type = "string",
                                },
                            },
                        },
                    },
                },
            },
        };

        return SpecHandlerModelBuilder
            .BuildModels(
                spec,
                "Fleet.Models",
                "Fleet.Services",
                "Fleet.Generated",
                "Fleet.Validation"
            )
            .Single()
            .RequestParameterInformationList.Single(parameter =>
                parameter.BindingType == ParameterBindType.Body
            );
    }

    /// <summary>
    /// A binary body is the bytes, bound as a real <c>byte[]</c> so the binder reads them all.
    /// </summary>
    [Fact]
    public void ABinaryBodyIsRaw()
    {
        var body = Body("string", "binary", "application/octet-stream");

        Assert.True(body.IsRawBody);
        Assert.True(body.ParameterType.IsArray);
    }

    /// <summary>Binary says the bytes whatever the media type names them as.</summary>
    [Fact]
    public void ABinaryBodyIsRawUnderAJsonMediaType()
    {
        Assert.True(Body("string", "binary", "application/json").IsRawBody);
    }

    /// <summary>
    /// A byte body under JSON is a base64 string inside a JSON document, which the JSON reader
    /// decodes.
    /// </summary>
    [Fact]
    public void AByteBodyUnderJsonIsReadAsJson()
    {
        Assert.False(Body("string", "byte", "application/json").IsRawBody);
        Assert.False(Body("string", "byte", "application/vnd.fleet+json").IsRawBody);
    }

    /// <summary>A byte body under a media type that is not JSON has no document to be inside.</summary>
    [Fact]
    public void AByteBodyUnderAnotherMediaTypeIsRaw()
    {
        Assert.True(Body("string", "byte", "image/jpeg").IsRawBody);
    }

    /// <summary>A string body is text, whatever it is sent as.</summary>
    [Fact]
    public void AStringBodyIsNotRaw()
    {
        Assert.False(Body("string", null, "text/plain").IsRawBody);
    }
}
