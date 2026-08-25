using System.Collections.Immutable;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Behaviours the attribute-routed table has and this one does not.
///
/// <para>
/// Every one of these fails today and none of them is fixed here. They are the acceptance criteria
/// for pointing this path at the attribute-routed generator: the fix is that there stops being a
/// second implementation, not that this one is patched to match.
/// </para>
/// </summary>
public class SpecRouteCorrectnessTests {
    private static string Generate(string scenario) {
        var (appModel, handlers) = SpecRouteCorpus.Build(scenario);

        return SpecRoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, ImmutableArray<HandlerInfo?>.Empty,
            ImmutableArray<SpecRegistration>.Empty, CancellationToken.None);
    }

    /// <summary>
    /// A catch-all takes the rest of the path, separators included. This table emits a guard that
    /// returns null the moment the remainder contains one, so <c>/files/a/b</c> does not reach
    /// <c>/files/{*path}</c> — it is matched as though the token were an ordinary single segment.
    /// </summary>
    [Fact]
    public void CatchAll_DoesNotRejectARemainderContainingASeparator() {
        var result = Generate("catch-all");

        Assert.DoesNotContain("IndexOf('/') >= 0", result);
    }

    /// <summary>
    /// The asterisk says how much of the path to take, not what to call it. Binding the marker into
    /// the name gives the handler a parameter named <c>*path</c>, which nothing declares.
    /// </summary>
    [Fact]
    public void CatchAll_BindsTheTokenWithoutTheMarker() {
        var result = Generate("catch-all");

        Assert.DoesNotContain("\"*path\"", result);
        Assert.Contains("\"path\"", result);
    }

    /// <summary>
    /// Token names are fixed at generation time, so the attribute-routed table hoists them into one
    /// static array per leaf and writes only the value on a match. Constructing a PathToken per
    /// match allocates on every request instead — which a behavioural test cannot see.
    /// </summary>
    [Fact]
    public void Tokens_AreWrittenIntoAStaticNamesArray() {
        var result = Generate("single-token");

        Assert.Contains("_pathTokenNames", result);
    }
}
