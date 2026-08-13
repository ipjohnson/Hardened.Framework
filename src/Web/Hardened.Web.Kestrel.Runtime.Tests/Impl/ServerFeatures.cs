using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Hardened.Web.Kestrel.Runtime.Tests.Impl;

/// <summary>
/// The feature collection a server hands to <c>IHttpApplication.CreateContext</c>.
///
/// Hand-rolled rather than taken from <c>DefaultHttpContext</c> because the response feature has
/// to be able to report a started response. Kestrel sets that flag on the first body write and
/// the adapter's status contract turns on it, but the stock <c>HttpResponseFeature</c> hardcodes
/// it to <c>false</c> with no way to change it.
/// </summary>
internal sealed class ServerFeatures {
    public ServerFeatures(string method = "GET", string path = "/test", string? queryString = null) {
        Request = new HttpRequestFeature {
            Method = method,
            Path = path,
            QueryString = queryString ?? ""
        };

        Body = new MemoryStream();
        Response = new TestResponseFeature();
        ResponseBody = new TestResponseBodyFeature(Body);

        // The stock HttpRequestLifetimeFeature.Abort() is a no-op -- a server supplies the real
        // implementation -- so aborting is driven through this source instead.
        Aborted = new CancellationTokenSource();
        Lifetime = new HttpRequestLifetimeFeature { RequestAborted = Aborted.Token };

        Collection = new FeatureCollection();
        Collection.Set<IHttpRequestFeature>(Request);
        Collection.Set<IHttpResponseFeature>(Response);
        Collection.Set<IHttpResponseBodyFeature>(ResponseBody);
        Collection.Set<IHttpRequestLifetimeFeature>(Lifetime);
    }

    public FeatureCollection Collection { get; }

    public HttpRequestFeature Request { get; }

    public TestResponseFeature Response { get; }

    public TestResponseBodyFeature ResponseBody { get; }

    public HttpRequestLifetimeFeature Lifetime { get; }

    /// <summary>Cancel this to simulate the client disconnecting mid-request.</summary>
    public CancellationTokenSource Aborted { get; }

    public MemoryStream Body { get; }

    internal sealed class TestResponseFeature : IHttpResponseFeature {
        public int StatusCode { get; set; } = 200;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        /// <summary>Settable, standing in for Kestrel flushing the response line and headers.</summary>
        public bool HasStarted { get; set; }

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }

    /// <summary>Records completion, which Kestrel requires and a response with no body relies on.</summary>
    internal sealed class TestResponseBodyFeature : IHttpResponseBodyFeature {
        public TestResponseBodyFeature(Stream stream) => Stream = stream;

        public int CompleteCount { get; private set; }

        public Stream Stream { get; }

        public PipeWriter Writer => PipeWriter.Create(Stream);

        public Task CompleteAsync() {
            CompleteCount++;
            return Task.CompletedTask;
        }

        public void DisableBuffering() { }

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
