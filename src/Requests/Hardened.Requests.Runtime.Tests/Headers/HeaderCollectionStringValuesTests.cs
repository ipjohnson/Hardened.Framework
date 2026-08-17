using Hardened.Requests.Runtime.Headers;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Headers;

/// <summary>
/// The header collection backing the API Gateway and streaming transports. Lookups that
/// miss must yield <see cref="StringValues.Empty"/> rather than throwing, because callers
/// index into it directly.
/// </summary>
public class HeaderCollectionStringValuesTests {

    [Fact]
    public void ConstructsFromStringDictionary() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> {
            { "Content-Type", "application/json" },
            { "Accept", "text/plain" }
        });

        Assert.Equal("application/json", headers.Get("Content-Type").ToString());
        Assert.Equal("text/plain", headers.Get("Accept").ToString());
        Assert.Equal(2, headers.Count);
    }

    [Fact]
    public void ConstructsEmptyFromNullDictionary() {
        var headers = new HeaderCollectionStringValues((IDictionary<string, string>?)null);

        Assert.Empty(headers);
    }

    [Fact]
    public void DefaultConstructorStartsEmpty() {
        Assert.Empty(new HeaderCollectionStringValues());
    }

    /// <summary>
    /// A dictionary that already compares names without regard to case is wrapped by reference, so a
    /// caller that hands one over and reads its own copy back — which is how the Lambda transports
    /// collect response headers — still sees the writes.
    /// </summary>
    [Fact]
    public void WrapsACaseInsensitiveDictionaryByReference() {
        var backing = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase) {
            { "X-Trace", "abc" }
        };

        var headers = new HeaderCollectionStringValues(backing);

        headers.Set("X-Extra", "value");

        Assert.True(backing.ContainsKey("X-Extra"));
    }

    /// <summary>
    /// One that does not is copied, and the reference is deliberately given up to get the lookups
    /// right. A case-sensitive store is how <c>content-type</c> from API Gateway failed to answer to
    /// <c>Content-Type</c>, which is what <c>KnownHeaders</c> asks for.
    /// </summary>
    [Fact]
    public void CopiesACaseSensitiveDictionaryRatherThanInheritingIt() {
        var backing = new Dictionary<string, StringValues> { { "content-type", "text/csv" } };

        var headers = new HeaderCollectionStringValues(backing);

        Assert.Equal("text/csv", headers.Get("Content-Type").ToString());

        headers.Set("X-Extra", "value");

        Assert.False(backing.ContainsKey("X-Extra"));
    }

    /// <summary>
    /// HTTP header names are case-insensitive, and every transport spells them differently: API
    /// Gateway lowercases, Kestrel preserves what arrived, a hand-written test writes whatever reads
    /// best. Every accessor has to agree.
    /// </summary>
    [Theory]
    [InlineData("content-type")]
    [InlineData("Content-Type")]
    [InlineData("CONTENT-TYPE")]
    [InlineData("cOnTeNt-TyPe")]
    public void EveryAccessorIgnoresCase(string spelling) {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> {
            { "content-type", "text/csv" }
        });

        Assert.Equal("text/csv", headers.Get(spelling).ToString());
        Assert.Equal("text/csv", headers[spelling].ToString());
        Assert.True(headers.ContainsKey(spelling));
        Assert.True(headers.TryGet(spelling, out var viaTryGet));
        Assert.Equal("text/csv", viaTryGet.ToString());
        Assert.True(headers.TryGetValue(spelling, out var viaTryGetValue));
        Assert.Equal("text/csv", viaTryGetValue.ToString());
    }

    [Fact]
    public void AppendingIgnoresCase() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> {
            { "accept", "text/csv" }
        });

        headers.Append("Accept", "text/html");

        Assert.Equal("text/csv,text/html", headers.Get("ACCEPT").ToString());
        Assert.Single(headers);
    }

    [Fact]
    public void RemovingIgnoresCase() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> {
            { "x-gone", "here" }
        });

        Assert.True(headers.Remove("X-Gone"));
        Assert.Empty(headers);
    }

    [Fact]
    public void SettingAnExistingHeaderInAnotherCaseReplacesIt() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> {
            { "accept", "text/csv" }
        });

        headers.Set("Accept", "text/html");

        Assert.Single(headers);
        Assert.Equal("text/html", headers.Get("accept").ToString());
    }

    /// <summary>
    /// The helper the transports use when they hold a raw dictionary rather than this collection —
    /// the header override a forked request carries on ASP.NET and Kestrel.
    /// </summary>
    [Fact]
    public void EnsureCaseInsensitiveKeepsADictionaryThatAlreadyIs() {
        var backing = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        Assert.Same(backing, HeaderCollectionStringValues.EnsureCaseInsensitive(backing));
    }

    [Fact]
    public void EnsureCaseInsensitiveCopiesOneThatIsNot() {
        var backing = new Dictionary<string, StringValues> { { "content-type", "text/csv" } };

        var ensured = HeaderCollectionStringValues.EnsureCaseInsensitive(backing);

        Assert.NotSame(backing, ensured);
        Assert.Equal("text/csv", ensured["Content-Type"].ToString());
    }

    [Fact]
    public void GetReturnsEmptyForMissingKey() {
        Assert.Equal(StringValues.Empty, new HeaderCollectionStringValues().Get("Nope"));
    }

    [Fact]
    public void IndexerReturnsEmptyForMissingKeyRatherThanThrowing() {
        Assert.Equal(StringValues.Empty, new HeaderCollectionStringValues()["Nope"]);
    }

    [Fact]
    public void IndexerSetThenGetRoundTrips() {
        var headers = new HeaderCollectionStringValues();

        headers["X-Custom"] = "one";

        Assert.Equal("one", headers["X-Custom"].ToString());
    }

    [Fact]
    public void TryGetReportsPresenceAndAbsence() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> { { "A", "1" } });

        Assert.True(headers.TryGet("A", out var found));
        Assert.Equal("1", found.ToString());

        Assert.False(headers.TryGet("B", out var missing));
        Assert.Equal(StringValues.Empty, missing);
    }

    [Fact]
    public void AppendCreatesTheHeaderWhenAbsent() {
        var headers = new HeaderCollectionStringValues();

        var result = headers.Append("Set-Cookie", "a=1");

        Assert.Equal("a=1", result.ToString());
        Assert.Equal("a=1", headers.Get("Set-Cookie").ToString());
    }

    [Fact]
    public void AppendConcatenatesOntoAnExistingHeader() {
        var headers = new HeaderCollectionStringValues();

        headers.Append("Set-Cookie", "a=1");
        var result = headers.Append("Set-Cookie", "b=2");

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "a=1", "b=2" }, result.ToArray());
    }

    [Fact]
    public void AppendTreatsNullAsEmptyString() {
        var headers = new HeaderCollectionStringValues();

        var result = headers.Append("X-Null", null!);

        Assert.Equal("", result.ToString());
    }

    [Fact]
    public void SetWithNullRemovesTheHeader() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> { { "X-Gone", "here" } });

        var result = headers.Set("X-Gone", (object?)null);

        Assert.Equal(StringValues.Empty, result);
        Assert.False(headers.ContainsKey("X-Gone"));
    }

    [Fact]
    public void SetConvertsNonStringValues() {
        var headers = new HeaderCollectionStringValues();

        headers.Set("Content-Length", 1234);

        Assert.Equal("1234", headers.Get("Content-Length").ToString());
    }

    [Fact]
    public void RemoveReportsWhetherTheKeyWasPresent() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> { { "A", "1" } });

        Assert.True(headers.Remove("A"));
        Assert.False(headers.Remove("A"));
    }

    [Fact]
    public void ClearEmptiesTheCollection() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> {
            { "A", "1" }, { "B", "2" }
        });

        headers.Clear();

        Assert.Empty(headers);
    }

    [Fact]
    public void KeysAndValuesExposeContents() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> { { "A", "1" } });

        Assert.Contains("A", headers.Keys);
        Assert.Contains(headers.Values, v => v.ToString() == "1");
    }

    [Fact]
    public void IsEnumerable() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> {
            { "A", "1" }, { "B", "2" }
        });

        var seen = headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());

        Assert.Equal("1", seen["A"]);
        Assert.Equal("2", seen["B"]);
    }

    [Fact]
    public void ToStringDictionaryFlattensValues() {
        var headers = new HeaderCollectionStringValues();
        headers.Append("Set-Cookie", "a=1");
        headers.Append("Set-Cookie", "b=2");

        var flattened = headers.ToStringDictionary();

        Assert.Equal("a=1,b=2", flattened["Set-Cookie"]);
    }

    [Fact]
    public void IsNotReadOnly() {
        Assert.False(new HeaderCollectionStringValues().IsReadOnly);
    }

    [Fact]
    public void ContainsMatchesOnKeyAndValue() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> { { "A", "1" } });

        Assert.Contains(new KeyValuePair<string, StringValues>("A", "1"), headers);
        Assert.DoesNotContain(new KeyValuePair<string, StringValues>("A", "2"), headers);
    }

    [Fact]
    public void AddAndRemoveKeyValuePairRoundTrip() {
        var headers = new HeaderCollectionStringValues();

        headers.Add(new KeyValuePair<string, StringValues>("A", "1"));
        Assert.Equal("1", headers.Get("A").ToString());

        Assert.True(headers.Remove(new KeyValuePair<string, StringValues>("A", "1")));
        Assert.Empty(headers);
    }

    [Fact]
    public void CopyToWritesIntoTheTargetArray() {
        var headers = new HeaderCollectionStringValues(new Dictionary<string, string> { { "A", "1" } });
        var target = new KeyValuePair<string, StringValues>[1];

        headers.CopyTo(target, 0);

        Assert.Equal("A", target[0].Key);
    }

    /// <summary>
    /// Add(string, StringValues) is unimplemented and throws. Pinned so the behaviour is
    /// visible rather than discovered at runtime - callers should use Set or the
    /// KeyValuePair overload. A failure here means it was implemented, which is an
    /// improvement worth updating this test for.
    /// </summary>
    [Fact]
    public void AddByKeyAndValueIsNotImplemented() {
        var headers = new HeaderCollectionStringValues();

        Assert.Throws<NotImplementedException>(() => headers.Add("A", new StringValues("1")));
    }
}
