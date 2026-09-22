using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Runtime.Forms;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using static Hardened.Requests.Runtime.Tests.Forms.MultipartBodies;
using ValidationException = Hardened.Requests.Runtime.Validation.ValidationException;

namespace Hardened.Requests.Runtime.Tests.Forms;

public class FormReaderTests
{
    private const string Multipart = "multipart/form-data; boundary=" + Boundary;

    /// <summary>
    /// A context over <paramref name="body"/>, with a request scope holding the per-request cache
    /// unless <paramref name="cached"/> says otherwise.
    /// </summary>
    private static IExecutionContext Context(
        string? contentType,
        Stream body,
        long? maxBodyBytes = null,
        bool cached = true
    )
    {
        var services = new ServiceCollection();

        if (cached)
        {
            services.AddScoped<RequestFormCache>();
        }

        if (maxBodyBytes is { } limit)
        {
            services.AddSingleton(
                Options.Create<IFormConfiguration>(new FormConfiguration { MaxBodyBytes = limit })
            );
        }

        var root = services.BuildServiceProvider();
        var scope = root.CreateScope();

        var request = new TestExecutionRequest(
            "POST",
            "/form",
            "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())
        )
        {
            Body = body,
        };

        if (contentType != null)
        {
            request.Headers["Content-Type"] = contentType;
        }

        return new TestExecutionContext(
            root,
            scope.ServiceProvider,
            Substitute.For<IKnownServices>(),
            request,
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None
        );
    }

    private static ValueTask<IFormCollection> Read(IExecutionContext context) =>
        new FormReader().ReadForm(context);

    /// <summary>
    /// jQuery and axios send the charset as a parameter, and a form sent that way read as empty.
    /// </summary>
    [Theory]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/x-www-form-urlencoded; charset=UTF-8")]
    [InlineData("Application/X-WWW-Form-UrlEncoded ;charset=utf-8")]
    public async Task AUrlEncodedBodyIsReadWhateverItsParameters(string contentType)
    {
        var form = await Read(
            Context(contentType, new MemoryStream("a=1&b=two+words"u8.ToArray()))
        );

        Assert.Equal("1", form.Get("a").ToString());
        Assert.Equal("two words", form.Get("b").ToString());
    }

    [Fact]
    public async Task ABodyWithNoContentTypeReadsAsUrlEncoded()
    {
        var form = await Read(Context(null, new MemoryStream("a=1"u8.ToArray())));

        Assert.Equal("1", form.Get("a").ToString());
    }

    [Fact]
    public async Task AnotherContentTypeReadsAsAnEmptyForm()
    {
        var form = await Read(Context("application/json", new MemoryStream("{}"u8.ToArray())));

        Assert.Same(EmptyFormCollection.Instance, form);
    }

    /// <summary>
    /// Forward-only, because four of the hosts hand over a stream that cannot seek or report its
    /// length.
    /// </summary>
    [Fact]
    public async Task AMultipartBodyIsReadFromAStreamThatCannotSeek()
    {
        var form = await Read(Context(Multipart, new ForwardOnlyStream(RequestBench())));

        Assert.Equal("qwertyuiopas", form.Get("tenant").ToString());
        Assert.Equal(Csv.Length, form.GetFile("file")!.Length);
    }

    /// <summary>A body larger than the first buffer is read past it.</summary>
    [Fact]
    public async Task ALargeFileIsReadWhole()
    {
        var large = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();

        var form = await Read(Context(Multipart, new ForwardOnlyStream(RequestBench(large))));

        using var copy = new MemoryStream();

        form.GetFile("file")!.OpenReadStream().CopyTo(copy);

        Assert.Equal(large, copy.ToArray());
    }

    /// <summary>
    /// The form is read once per request. A second reader gets the first reading, rather than an
    /// empty form from a stream that has already been consumed.
    /// </summary>
    [Fact]
    public async Task ASecondReadReturnsTheFirst()
    {
        var context = Context(Multipart, new ForwardOnlyStream(RequestBench()));

        var first = await Read(context);
        var second = await Read(context);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task AUrlEncodedFormIsKeptForTheRequestToo()
    {
        var context = Context(
            "application/x-www-form-urlencoded",
            new ForwardOnlyStream("a=1"u8.ToArray())
        );

        Assert.Same(await Read(context), await Read(context));
    }

    /// <summary>Without a request scope to keep it in, the form is still read.</summary>
    [Fact]
    public async Task AFormIsReadWithoutACache()
    {
        var form = await Read(Context(Multipart, new MemoryStream(RequestBench()), cached: false));

        Assert.Equal("qwertyuiopas", form.Get("tenant").ToString());
    }

    [Fact]
    public async Task ABodyPastTheCapIsA413()
    {
        var exception = await Assert.ThrowsAsync<FormBodyTooLargeException>(async () =>
            await Read(Context(Multipart, new ForwardOnlyStream(RequestBench()), maxBodyBytes: 100))
        );

        Assert.Equal(413, exception.StatusCode);
        Assert.Equal(100, exception.Limit);
        Assert.Contains("100 bytes", exception.Message);
    }

    [Fact]
    public async Task ABodyPastTheCapIsA413WithoutACacheToo()
    {
        await Assert.ThrowsAsync<FormBodyTooLargeException>(async () =>
            await Read(Context(Multipart, new MemoryStream(RequestBench()), 100, cached: false))
        );
    }

    /// <summary>
    /// The refusal a malformed JSON body gets, so a malformed form reads the same however it was
    /// caught.
    /// </summary>
    [Fact]
    public async Task AMalformedBodyIsAnInvalidBody()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(async () =>
            await Read(Context(Multipart, new MemoryStream("not multipart"u8.ToArray())))
        );

        var error = Assert.Single(exception.ValidationResult.Errors);

        Assert.Equal("body", error.Field);
        Assert.Equal("invalid", error.Code);
        Assert.IsType<FormatException>(exception.InnerException);
    }

    [Fact]
    public async Task AMalformedBodyIsAnInvalidBodyWithoutACacheToo()
    {
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await Read(Context(Multipart, new MemoryStream("x"u8.ToArray()), cached: false))
        );
    }

    [Fact]
    public async Task AMultipartBodyWithNoBoundaryIsAnInvalidBody()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(async () =>
            await Read(Context("multipart/form-data", new MemoryStream(RequestBench())))
        );

        Assert.Contains("boundary", Assert.Single(exception.ValidationResult.Errors).Message);
    }

    /// <summary>
    /// From the start, because a filter ahead of the binder may have read a seekable body.
    /// </summary>
    [Fact]
    public async Task ASeekableBodyIsReadFromTheStart()
    {
        var body = new MemoryStream(RequestBench());

        body.Position = body.Length;

        var form = await Read(Context(Multipart, body));

        Assert.Equal("qwertyuiopas", form.Get("tenant").ToString());
    }

    /// <summary>
    /// The reader is a singleton, so the cap it reads on the first request holds for the next.
    /// </summary>
    [Fact]
    public async Task OneReaderServesManyRequests()
    {
        var reader = new FormReader();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var form = await reader.ReadForm(
                Context(Multipart, new MemoryStream(RequestBench()), maxBodyBytes: 1_000_000)
            );

            Assert.Equal("qwertyuiopas", form.Get("tenant").ToString());
        }
    }

    /// <summary>The scope ending gives the buffer back and forgets the form.</summary>
    [Fact]
    public async Task TheCacheIsClearedWhenTheScopeEnds()
    {
        var context = Context(Multipart, new MemoryStream(RequestBench()));
        var cache = context.RequestServices.GetRequiredService<RequestFormCache>();

        await Read(context);

        Assert.NotNull(cache.Form);

        cache.Dispose();
        cache.Dispose();

        Assert.Null(cache.Form);
    }

    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;

        public ForwardOnlyStream(byte[] bytes)
        {
            _inner = new MemoryStream(bytes);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        // A few bytes at a time, the way a socket hands a body over.
        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(count, 997));

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
