using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The encoded form of a case set, which is what crosses the incremental generator's cache.
///
/// <para>
/// The case list is carried on <c>ResponseInformationModel</c> as a string rather than a list,
/// because that type is a <c>record</c> whose synthesized equality is the cache key and a
/// <c>List&lt;T&gt;</c> member would compare by reference there. That makes this encoding
/// load-bearing in a way an ordinary serialization is not: a round trip that loses a field produces
/// a handler dispatching on a response set it does not have, and it surfaces as a caching problem
/// rather than as anything that looks related.
/// </para>
/// </summary>
public class UnionResponseEncodingTests {

    [Fact]
    public void RoundTrip_KeepsEveryFieldOfEveryCase() {
        var cases = new[] {
            new UnionCaseModel("global::App.Todo", 200, appliesHeaders: false, hasBody: true),
            new UnionCaseModel("global::App.NotFound", 404, appliesHeaders: false, hasBody: true),
            new UnionCaseModel("global::App.RateLimited", 429, appliesHeaders: true, hasBody: true),
            new UnionCaseModel("global::App.NoContent", 204, appliesHeaders: false, hasBody: false)
        };

        var decoded = UnionResponseSelector.Decode(UnionResponseSelector.Encode(cases));

        Assert.Equal(cases.Length, decoded.Count);

        for (var i = 0; i < cases.Length; i++) {
            Assert.Equal(cases[i].TypeName, decoded[i].TypeName);
            Assert.Equal(cases[i].Status, decoded[i].Status);
            Assert.Equal(cases[i].AppliesHeaders, decoded[i].AppliesHeaders);
            Assert.Equal(cases[i].HasBody, decoded[i].HasBody);
        }
    }

    /// <summary>
    /// Order is the declared order and has to survive, because it is the order the arms are emitted
    /// in and that is what makes the generated file readable against the signature.
    /// </summary>
    [Fact]
    public void RoundTrip_KeepsTheDeclaredOrder() {
        var cases = new[] {
            new UnionCaseModel("global::A", 200, false, true),
            new UnionCaseModel("global::B", 404, false, true),
            new UnionCaseModel("global::C", 409, false, true)
        };

        var decoded = UnionResponseSelector.Decode(UnionResponseSelector.Encode(cases));

        Assert.Equal(new[] { "global::A", "global::B", "global::C" },
            decoded.Select(c => c.TypeName));
    }

    /// <summary>
    /// Two case sets differing only in a status must encode differently, or the cache reports them
    /// equal and the generator serves the dispatch it already emitted.
    /// </summary>
    [Fact]
    public void Encoding_DistinguishesSetsThatDifferOnlyByStatus() {
        var first = UnionResponseSelector.Encode(
            new[] { new UnionCaseModel("global::A", 404, false, true) });

        var second = UnionResponseSelector.Encode(
            new[] { new UnionCaseModel("global::A", 410, false, true) });

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Encoding_DistinguishesSetsThatDifferOnlyByHeaderContribution() {
        var first = UnionResponseSelector.Encode(
            new[] { new UnionCaseModel("global::A", 401, appliesHeaders: true, hasBody: true) });

        var second = UnionResponseSelector.Encode(
            new[] { new UnionCaseModel("global::A", 401, appliesHeaders: false, hasBody: true) });

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Encoding_DistinguishesSetsThatDifferOnlyByBody() {
        var first = UnionResponseSelector.Encode(
            new[] { new UnionCaseModel("global::A", 204, false, hasBody: true) });

        var second = UnionResponseSelector.Encode(
            new[] { new UnionCaseModel("global::A", 204, false, hasBody: false) });

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Null and empty both mean "not a response set", and the emitter asks for the count rather than
    /// for null - so decoding either has to give it nothing rather than throw.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Decode_TreatsNothingAsNoCases(string? encoded) {
        Assert.Empty(UnionResponseSelector.Decode(encoded));
    }
}
