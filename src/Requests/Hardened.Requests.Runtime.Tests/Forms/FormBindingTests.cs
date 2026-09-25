using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Errors;
using Hardened.Requests.Runtime.Forms;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Forms;

public class FormBindingTests
{
    private static readonly IFormCollection Form = new SimpleFormCollection(
        new Dictionary<string, StringValues> { ["a"] = "1" }
    );

    /// <summary>A context whose form reader answers <see cref="Form"/>.</summary>
    private static IExecutionContext Context(string? contentType)
    {
        var reader = Substitute.For<IFormReader>();

        reader.ReadForm(Arg.Any<IExecutionContext>()).Returns(new ValueTask<IFormCollection>(Form));

        var knownServices = Substitute.For<IKnownServices>();

        knownServices.FormReader.Returns(reader);

        var request = new TestExecutionRequest(
            "POST",
            "/form",
            null,
            new SimpleQueryStringCollection(new Dictionary<string, string>())
        )
        {
            Body = new MemoryStream(),
        };

        if (contentType != null)
        {
            request.Headers[KnownHeaders.ContentType] = contentType;
        }

        var services = new ServiceCollection().BuildServiceProvider();

        return new TestExecutionContext(
            services,
            services,
            knownServices,
            request,
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None
        );
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/x-www-form-urlencoded; charset=UTF-8")]
    [InlineData("multipart/form-data; boundary=xyz")]
    [InlineData("Multipart/Form-Data; boundary=xyz")]
    public async Task AFormIsReadThroughTheReader(string contentType)
    {
        Assert.Same(Form, await FormBinding.Read(Context(contentType)));
    }

    /// <summary>No content type reads as url-encoded, as it does in the reader.</summary>
    [Fact]
    public async Task ABodyWithNoContentTypeIsRead()
    {
        Assert.Same(Form, await FormBinding.Read(Context(null)));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/plain; charset=utf-8")]
    [InlineData("application/x-www-form-urlencoded-extra")]
    public async Task AnotherContentTypeIsA415(string contentType)
    {
        var exception = await Assert.ThrowsAsync<UnsupportedContentTypeException>(async () =>
            await FormBinding.Read(Context(contentType))
        );

        Assert.Equal(415, exception.StatusCode);
        Assert.Equal(contentType, exception.ContentType);
        Assert.Equal(FormBinding.ContentTypes, exception.Supported);
    }

    /// <summary>The body is not read at all when the content type already refuses it.</summary>
    [Fact]
    public async Task A415DoesNotReadTheBody()
    {
        var context = Context("application/json");

        await Assert.ThrowsAsync<UnsupportedContentTypeException>(async () =>
            await FormBinding.Read(context)
        );

        Assert.Empty(context.KnownServices.FormReader.ReceivedCalls());
    }

    [Fact]
    public async Task The415NamesWhatWasSentAndWhatIsRead()
    {
        var exception = await Assert.ThrowsAsync<UnsupportedContentTypeException>(async () =>
            await FormBinding.Read(Context("application/json"))
        );

        Assert.Equal(
            "This route does not read application/json. It reads "
                + "application/x-www-form-urlencoded, multipart/form-data.",
            exception.Message
        );
    }

    /// <summary>RFC 9110 suggests an Accept header naming what would have been read.</summary>
    [Fact]
    public void The415CarriesAnAcceptHeader()
    {
        var headers = new Dictionary<string, StringValues>();

        new UnsupportedContentTypeException(
            "application/json",
            FormBinding.ContentTypes
        ).ApplyHeaders(headers);

        Assert.Equal(
            "application/x-www-form-urlencoded, multipart/form-data",
            headers[KnownHeaders.Accept].ToString()
        );
    }
}
