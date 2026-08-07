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

    [Fact]
    public void WrapsAnExistingStringValuesDictionaryByReference() {
        var backing = new Dictionary<string, StringValues> { { "X-Trace", "abc" } };
        var headers = new HeaderCollectionStringValues(backing);

        headers.Set("X-Extra", "value");

        Assert.True(backing.ContainsKey("X-Extra"));
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
