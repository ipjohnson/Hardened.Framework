using Hardened.Requests.Abstract.Execution;
using Xunit;

namespace Hardened.Requests.Testing.Conformance;

/// <summary>
/// The web-shaped profile: everything in
/// <see cref="PayloadExecutionRequestConformanceTests"/>, plus what only a request that arrived
/// over HTTP can answer.
///
/// <para>
/// Three assertions, and they are the ones that need the transport to have carried a value rather
/// than merely to expose a property. A query string and a cookie header exist on the wire; a queue
/// message has neither, so its adapter answers empty and always will. Splitting them out is what
/// lets a payload-shaped adapter enrol in the twenty-three that do apply to it, rather than skip
/// three tests it can never pass - and a skipped test is one this repository fails CI on.
/// </para>
///
/// <para>
/// The name is unprefixed and the payload one is not, which is the wrong way round to read but the
/// right way round to change: this is the class the four enrolled transports already derive from,
/// and <c>Hardened.Requests.Testing</c> is a shipped package. Renaming it would break every
/// enrolment outside this repository to gain a symmetry.
/// </para>
///
/// To enrol a web transport, derive from this class and supply an adapter:
///
/// <code>
/// public class MyTransportConformanceTests : ExecutionRequestConformanceTests {
///     protected override IExecutionRequestConformanceAdapter Adapter { get; } = new MyAdapter();
/// }
/// </code>
/// </summary>
public abstract class ExecutionRequestConformanceTests : PayloadExecutionRequestConformanceTests {

    [Fact]
    public void QueryStringIsSurfaced() {
        var request = Create(s => {
            s.QueryString["page"] = "2";
            s.QueryString["size"] = "50";
        });

        Assert.Equal("2", request.QueryString.Get("page").ToString());
        Assert.Equal("50", request.QueryString.Get("size").ToString());
    }

    /// <summary>
    /// A query value arrives decoded, whatever the transport had to do to carry it.
    /// </summary>
    /// <remarks>
    /// The characters here are the ones that told the transports apart. A timestamp's <c>:</c> and
    /// <c>+</c> have to survive percent-encoding; base64's trailing <c>=</c> has to survive a
    /// parser that splits on <c>=</c>; and a space has to arrive as a space whether it was written
    /// <c>%20</c> or <c>+</c>. The test host used to fail all three and answer 400 where Kestrel
    /// answered 200.
    /// </remarks>
    [Fact]
    public void QueryStringValuesArriveDecoded() {
        var request = Create(s => {
            s.QueryString["asOf"] = "2026-09-10T09:00:00+00:00";
            s.QueryString["cursor"] = "YWJjZA==";
            s.QueryString["title"] = "East of Eden";
        });

        Assert.Equal("2026-09-10T09:00:00+00:00", request.QueryString.Get("asOf").ToString());
        Assert.Equal("YWJjZA==", request.QueryString.Get("cursor").ToString());
        Assert.Equal("East of Eden", request.QueryString.Get("title").ToString());
    }

    [Fact]
    public void CookiesAreSurfaced() {
        var request = Create(s => {
            s.Cookies.Add("session=abc123");
            s.Cookies.Add("theme=dark");
        });

        Assert.Contains(request.Cookies, c => c.Contains("session=abc123"));
        Assert.Contains(request.Cookies, c => c.Contains("theme=dark"));
    }
}
