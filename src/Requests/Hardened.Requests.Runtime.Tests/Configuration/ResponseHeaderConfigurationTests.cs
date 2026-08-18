using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Configuration;

/// <summary>
/// What an application adds to every response.
/// </summary>
/// <remarks>
/// The configuration object itself was at 25% — only the string overload of <c>Add</c> was ever
/// called. <c>IOFilterProviderTests</c> covers how these reach a response; this covers the
/// collection, including that the two <c>Add</c> families accumulate independently and keep the
/// order they were declared in. Order matters for the common security headers, where a later
/// entry under the same name is meant to win.
/// </remarks>
public class ResponseHeaderConfigurationTests {

    [Fact]
    public void AFreshConfigurationHasNothing() {
        var configuration = new ResponseHeaderConfiguration();

        Assert.Empty(configuration.CommonHeaders);
        Assert.Empty(configuration.HeaderActions);
    }

    [Fact]
    public void AStringHeaderIsRecorded() {
        var configuration = new ResponseHeaderConfiguration();

        configuration.Add("X-Frame-Options", "DENY");

        var header = Assert.Single(configuration.CommonHeaders);

        Assert.Equal("X-Frame-Options", header.Key);
        Assert.Equal("DENY", header.Value.ToString());
    }

    [Fact]
    public void AMultiValueHeaderKeepsEveryValue() {
        var configuration = new ResponseHeaderConfiguration();

        configuration.Add("Vary", new StringValues(["Accept", "Accept-Encoding"]));

        var vary = Assert.Single(configuration.CommonHeaders).Value;

        Assert.Equal(2, vary.Count);
        Assert.Equal("Accept", vary[0]);
        Assert.Equal("Accept-Encoding", vary[1]);
    }

    /// <summary>
    /// Recorded rather than merged — the writer assigns in order, so the later entry wins.
    /// </summary>
    [Fact]
    public void TheSameHeaderAddedTwiceIsRecordedTwiceInOrder() {
        var configuration = new ResponseHeaderConfiguration();

        configuration.Add("X-Frame-Options", "SAMEORIGIN");
        configuration.Add("X-Frame-Options", "DENY");

        Assert.Equal(2, configuration.CommonHeaders.Count);
        Assert.Equal("DENY", configuration.CommonHeaders[1].Value.ToString());
    }

    [Fact]
    public void AnActionIsRecorded() {
        var configuration = new ResponseHeaderConfiguration();

        configuration.Add(_ => { });

        Assert.Single(configuration.HeaderActions);
    }

    [Fact]
    public void ActionsKeepTheOrderTheyWereAddedIn() {
        var configuration = new ResponseHeaderConfiguration();
        var log = new List<string>();

        configuration.Add(_ => log.Add("first"));
        configuration.Add(_ => log.Add("second"));

        foreach (var action in configuration.HeaderActions) {
            action.Invoke(null!);
        }

        Assert.Equal(["first", "second"], log);
    }

    [Fact]
    public void ActionsAndCommonHeadersAccumulateIndependently() {
        var configuration = new ResponseHeaderConfiguration();

        configuration.Add("X-Frame-Options", "DENY");
        configuration.Add((IExecutionContext _) => { });

        Assert.Single(configuration.CommonHeaders);
        Assert.Single(configuration.HeaderActions);
    }

    /// <summary>
    /// The interface is what <c>IOFilterProvider</c> reads. It must see the same lists rather than
    /// a copy taken when the configuration was built.
    /// </summary>
    [Fact]
    public void TheInterfaceSeesLaterAdditions() {
        var configuration = new ResponseHeaderConfiguration();
        IResponseHeaderConfiguration asInterface = configuration;

        configuration.Add("X-Frame-Options", "DENY");

        Assert.Single(asInterface.CommonHeaders);
    }
}
