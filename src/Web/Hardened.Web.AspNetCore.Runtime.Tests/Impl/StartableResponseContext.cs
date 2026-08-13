using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Hardened.Web.AspNetCore.Runtime.Tests.Impl;

/// <summary>
/// An <see cref="HttpContext"/> whose <c>HasStarted</c> can be switched on.
///
/// <see cref="DefaultHttpContext"/>'s stock response feature hardcodes <c>HasStarted</c> to
/// <c>false</c> and offers no way to change it, so anything depending on the difference between a
/// response that has been flushed and one that has not cannot be tested against it. Kestrel flips
/// the flag on the first body write; this exposes it directly.
/// </summary>
internal static class StartableResponseContext {

    public static DefaultHttpContext Create(IServiceProvider requestServices, out Action start) {
        var features = new FeatureCollection();
        var responseFeature = new StartableResponseFeature();

        features.Set<IHttpRequestFeature>(new HttpRequestFeature { Method = "GET", Path = "/test" });
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));

        start = () => responseFeature.HasStarted = true;

        return new DefaultHttpContext(features) { RequestServices = requestServices };
    }

    private sealed class StartableResponseFeature : IHttpResponseFeature {
        public int StatusCode { get; set; } = 200;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted { get; set; }

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
