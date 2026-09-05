using System.Net;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

/// <summary>
/// The client half of every socket host: a real server started by the derived class over the
/// test's container, and an <see cref="HttpClient"/> the harness sends through to reach it.
/// </summary>
/// <remarks>
/// <para>
/// A derived class supplies two things: <see cref="Listen"/>, which starts the server on a
/// loopback port the kernel picks and returns the address it bound, and <see cref="StopAsync"/>,
/// which stops it within the bound it is given. Everything a test sees is here: the handler a
/// client's chain ends in, the request <see cref="ITestWebApp"/> sends, the response it reads,
/// and <see cref="LastResponse"/> recorded from what came back over the wire, because on a socket
/// the server side runs on the server's threads where no test is in scope.
/// </para>
/// <para>
/// One <see cref="SocketsHttpHandler"/> for the whole host, shared by every client the harness
/// hands out through a wrapper that does not dispose it: disposing the host closes every
/// connection its clients opened in one place, before the server is asked to stop, so the server
/// has nothing in flight and stops at once. The transport decompresses nothing, follows no
/// redirect and keeps no cookie jar, so a client sees the bytes, the status and the
/// <c>Set-Cookie</c> the wire carried; <see cref="TestWebResponse"/> undoes a content coding when
/// it reads. The client has no timeout of its own: the test's token governs, so a timeout reports
/// as the test's rather than as the transport's.
/// </para>
/// <para>
/// Disposal is in a fixed order: the transport first, then the server stopped with a token
/// bounded by <see cref="StopBound"/>, then the server disposed. A graceful stop with no bound
/// waits on a handler that never completes for ever; with the bound, Kestrel aborts what is left
/// when it fires.
/// </para>
/// </remarks>
public abstract class SocketHost : ITestHost {

    /// <summary>
    /// How long a server is given to stop gracefully before what is left is aborted. Settable,
    /// for a test of the bound itself; a suite has no reason to change it.
    /// </summary>
    public static TimeSpan StopBound { get; set; } = TimeSpan.FromSeconds(10);

    private readonly SocketsHttpHandler _transport = new() {
        AutomaticDecompression = DecompressionMethods.None,
        AllowAutoRedirect = false,
        UseCookies = false,
    };

    private HttpClient? _client;
    private Uri? _address;

    public abstract bool IsTerminal { get; }

    /// <summary>The address the server bound, with the port the kernel picked. Available once started.</summary>
    public Uri BaseAddress =>
        _address ?? throw new InvalidOperationException("The host has not started, so it has no address yet.");

    public async Task StartAsync(IServiceProvider provider, CancellationToken cancellationToken) {
        var bound = await Listen(provider, cancellationToken);

        // Kestrel reports what it bound as http://127.0.0.1:port; a base address needs the slash
        // for a relative path to resolve under it.
        _address = new Uri(bound.ToString().TrimEnd('/') + "/");

        _client = new HttpClient(CreateHandler(credential: null)) {
            BaseAddress = _address,
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>
    /// Starts the server over <paramref name="provider"/>, the test's own container, on a loopback
    /// port the kernel picks, and returns the address it bound. Read back from the server after it
    /// started, never computed.
    /// </summary>
    protected abstract Task<Uri> Listen(IServiceProvider provider, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the server: gracefully while <paramref name="bounded"/> holds, and by aborting what is
    /// left once it fires. Every client connection is already closed when this is called.
    /// </summary>
    protected abstract Task StopAsync(CancellationToken bounded);

    /// <summary>
    /// A chain that applies the credential where a request carries neither test header, records
    /// what comes back for <see cref="LastResponse"/>, and sends through the host's one transport.
    /// </summary>
    public HttpMessageHandler CreateHandler(TestCredential? credential) =>
        new CredentialHandler(credential) {
            InnerHandler = new SocketRecordingHandler {
                InnerHandler = new SharedTransportHandler(_transport)
            }
        };

    public async Task<TestWebResponse> SendAsync(TestHostRequest request, CancellationToken cancellationToken) {
        var client = _client ?? throw new InvalidOperationException("The host has not started, so there is nothing to send to.");

        request.Credential?.ApplyTo(request.Headers);

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), new Uri(BaseAddress, request.PathAndQuery));

        var hasBody = request.Body != Stream.Null && (!request.Body.CanSeek || request.Body.Length > 0);
        HttpContent? content = hasBody ? new StreamContent(request.Body) : null;

        foreach (var header in request.Headers) {
            var values = header.Value.ToArray();

            // A content header is refused on the message and belongs on the content, which the
            // message's collection says by refusing it - the same call ToResponse makes the other
            // way. A content header on a request with no body gets an empty one to sit on.
            if (message.Headers.TryAddWithoutValidation(header.Key, values)) {
                continue;
            }

            content ??= new ByteArrayContent([]);
            content.Headers.TryAddWithoutValidation(header.Key, values);
        }

        message.Content = content;

        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return new TestWebResponse((int)response.StatusCode, Headers(response), new MemoryStream(bytes), failure: null);
    }

    public async ValueTask DisposeAsync() {
        _client?.Dispose();
        _transport.Dispose();

        if (_address != null) {
            using var bound = new CancellationTokenSource(StopBound);

            await StopAsync(bound.Token);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Response headers and content headers together, matched without regard to case, because
    /// both are headers on the response and only the transport draws the line between them.
    /// </summary>
    internal static Dictionary<string, StringValues> Headers(HttpResponseMessage response) {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in response.Headers) {
            headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        foreach (var header in response.Content.Headers) {
            headers[header.Key] = new StringValues(header.Value.ToArray());
        }

        return headers;
    }

    /// <summary>The host's transport, in a client's chain without being owned by it.</summary>
    private sealed class SharedTransportHandler : DelegatingHandler {
        public SharedTransportHandler(HttpMessageHandler transport) : base(transport) {
        }

        protected override void Dispose(bool disposing) {
            // Deliberately not the base, which would dispose the transport this host shares
            // between every client it built.
        }
    }

    /// <summary>The two test headers on a request that carries neither, the way the pipeline host applies them.</summary>
    private sealed class CredentialHandler : DelegatingHandler {
        private readonly TestCredential? _credential;

        public CredentialHandler(TestCredential? credential) {
            _credential = credential;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            _credential?.ApplyTo(request);

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// What came back over the wire, kept for <see cref="LastResponse"/>: the status, every
    /// header, and the body - read to the end here and handed on as the same bytes, except for an
    /// event stream, which never ends and is left to stream with its body recorded as empty.
    /// </summary>
    private sealed class SocketRecordingHandler : DelegatingHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var response = await base.SendAsync(request, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString();

            if (contentType != null && contentType.StartsWith(KnownContentType.EventStream, StringComparison.OrdinalIgnoreCase)) {
                LastResponse.Record((int)response.StatusCode, Headers(response), contentType, []);

                return response;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var buffered = new ByteArrayContent(bytes);

            foreach (var header in response.Content.Headers) {
                buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content = buffered;

            LastResponse.Record((int)response.StatusCode, Headers(response), contentType, bytes);

            return response;
        }
    }
}
