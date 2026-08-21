using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Responses;
using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Tests.Responses;

/// <summary>
/// The branches CI's coverage report named as still uncovered, and nothing else.
/// </summary>
/// <remarks>
/// Chosen from the report rather than guessed at. The first pass at this release's coverage
/// shortfall was written from reading the new code and moved the number by a tenth of a point,
/// because the branches it covered were mostly covered already - the four classes below were what
/// actually remained, and three of them are nowhere near the response types the release is named
/// for.
/// </remarks>
public class RemainingBranchTests {

    /// <summary>
    /// The generic 503 with no retry hint sets no Retry-After.
    /// </summary>
    /// <remarks>
    /// Its non-generic twin already covered both sides of this. The two carry the same conditional
    /// written out twice, which is exactly the shape where covering one and not the other looks
    /// like coverage.
    /// </remarks>
    [Fact]
    public void AGenericServiceUnavailableWithoutARetryHintSetsNoHeader() {
        var headers = new Dictionary<string, StringValues>();

        ((IProvidesResponseHeaders)new ServiceUnavailable<string>("down")).ApplyHeaders(headers);

        Assert.Empty(headers);
    }

    /// <summary>And with one, it does - the side that was already covered, kept beside it.</summary>
    [Fact]
    public void AGenericServiceUnavailableWithARetryHintSetsIt() {
        var headers = new Dictionary<string, StringValues>();

        ((IProvidesResponseHeaders)new ServiceUnavailable<string>("down", TimeSpan.FromSeconds(30)))
            .ApplyHeaders(headers);

        Assert.Equal("30", headers["Retry-After"]);
    }

    /// <summary>
    /// A predicate requirement with no description renders as "predicate".
    /// </summary>
    /// <remarks>
    /// What a denied-authorization log line shows for a requirement whose author did not name it,
    /// which is the common case and the one nobody asserts.
    /// </remarks>
    [Fact]
    public void AnUndescribedPredicateRendersAsPredicate() {
        var requirement = Requirement.Predicate((_, _) => true);

        Assert.Equal("predicate", requirement.ToString());
    }

    [Fact]
    public void ADescribedPredicateRendersItsDescription() {
        var requirement = Requirement.Predicate((_, _) => true, "owns the record");

        Assert.Equal("owns the record", requirement.ToString());
    }

    /// <summary>
    /// A composite renders with the operator it actually is.
    /// </summary>
    /// <remarks>
    /// One expression picks the separator for both kinds, so a composite that rendered "&amp;" for
    /// an any-of would misreport every denial it appears in. Both sides asserted together, because
    /// the bug is the two disagreeing rather than either alone being wrong.
    /// </remarks>
    [Fact]
    public void AnAllOfRendersWithAmpersand() {
        var requirement = Requirement.AllOf(Requirement.Grant("read"), Requirement.Grant("write"));

        Assert.Equal("(read & write)", requirement.ToString());
    }

    [Fact]
    public void AnAnyOfRendersWithPipe() {
        var requirement = Requirement.AnyOf(Requirement.Grant("read"), Requirement.Grant("write"));

        Assert.Equal("(read | write)", requirement.ToString());
    }
}
