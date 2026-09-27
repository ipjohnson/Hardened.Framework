using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Logging;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// The 406, written the way every other refusal is.
/// </summary>
/// <remarks>
/// The 0.41 trial's B-11: an application's own <c>IExceptionToModelConverter</c> changed the
/// validation 400, a thrown 500 and the timeout 504, and did not reach the 406, which was written
/// straight to the response. Driven through the real serialization service, exception serializer
/// and locator, because what is asserted is that they meet.
/// </remarks>
public class NotAcceptableResponseTests
{
    private sealed record Product(string Sku);

    private sealed record OwnRefusal(string Code);

    private static ContextSerializationService Service(IExceptionToModelConverter converter)
    {
        var json = new SystemTextJsonResponseSerializer(
            Options.Create<IJsonSerializerConfiguration>(
                new JsonSerializerConfiguration
                {
                    SerializeOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web),
                }
            ),
            Array.Empty<IJsonTypeInfoResolver>()
        );

        var locator = new SerializationLocatorService(
            Array.Empty<IRequestDeserializer>(),
            [json],
            new ContentNegotiationPolicy(ContentNegotiationMode.Strict)
        );

        return new ContextSerializationService(
            NullLogger<ContextSerializationService>.Instance,
            locator,
            Substitute.For<INullValueResponseHandler>(),
            new ExceptionResponseSerializer(Substitute.For<IRequestLogger>(), locator, converter)
        );
    }

    private static IExecutionContext Context()
    {
        var context = Pipeline.Context(accept: "application/xml");

        context.Response.ResponseValue = new Product("1");
        context.HandlerInfo = new ExecutionRequestHandlerInfo(
            "/products/{sku}",
            "GET",
            typeof(NotAcceptableResponseTests),
            "Get",
            producedContentTypes: [KnownContentType.Json]
        );

        return context;
    }

    private static string Body(IExecutionContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    [Fact]
    public async Task AnApplicationsOwnConverterDecidesThe406()
    {
        var converter = Substitute.For<IExceptionToModelConverter>();

        converter
            .ConvertExceptionToModel(Arg.Any<IExecutionContext>(), Arg.Any<Exception>())
            .Returns((406, (object)new OwnRefusal("unacceptable")));

        var context = Context();

        await Service(converter).SerializeResponse(context);

        Assert.Equal(406, context.Response.Status);
        Assert.Equal("""{"code":"unacceptable"}""", Body(context));
        Assert.Equal(KnownContentType.Json, context.Response.ContentType);
    }

    /// <summary>
    /// With the stock converter the 406 still names what the operation produces.
    /// </summary>
    [Fact]
    public async Task TheStockConverterNamesWhatIsProduced()
    {
        var context = Context();

        await Service(new ExceptionToModelConverter()).SerializeResponse(context);

        Assert.Equal(406, context.Response.Status);

        using var body = JsonDocument.Parse(Body(context));

        Assert.Equal("NotAcceptable", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("application/json", body.RootElement.GetProperty("details").GetString());
    }
}
