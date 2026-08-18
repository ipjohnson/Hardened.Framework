using Hardened.Requests.Abstract.PathTokens;
using Hardened.Requests.Runtime.PathTokens;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.PathTokens;

/// <summary>
/// Path token values for a matched route.
/// </summary>
/// <remarks>
/// <para>
/// The class carries two constructors that mean different things, and the difference is the whole
/// design: the route-supplied names array is <em>shared across every request that matches that
/// route</em>, so it must never be written to, while the legacy constructor allocates its own and
/// may be. <c>_ownsNames</c> is what separates them, and the branch it guards was untaken —
/// the type sat at 76% line and 68% branch.
/// </para>
/// <para>
/// <see cref="SetOnARouteSuppliedCollectionDoesNotWriteTheSharedNamesArray"/> is the one that
/// matters. If that guard were dropped, one request could rename a token for every other request
/// matching the same route, concurrently, and nothing else in the suite would notice.
/// </para>
/// </remarks>
public class PathTokenCollectionTests {

    [Fact]
    public void EmptyHasNoTokens() {
        Assert.Equal(0, PathTokenCollection.Empty.Count);
    }

    [Fact]
    public void CountIsTheCountItWasBuiltWith() {
        Assert.Equal(3, new PathTokenCollection(3, ["a", "b", "c"]).Count);
    }

    [Fact]
    public void AValueSetPositionallyReadsBackUnderTheRoutesName() {
        var tokens = new PathTokenCollection(2, ["id", "postId"]);

        tokens.SetValue(0, "7");
        tokens.SetValue(1, "42");

        Assert.Equal("id", tokens.Get(0).TokenName);
        Assert.Equal("7", tokens.Get(0).TokenValue);
        Assert.Equal("42", tokens.Get(1).TokenValue);
    }

    /// <summary>
    /// The last value is filled in by the constructor because the match unwinds from the leaf.
    /// </summary>
    [Fact]
    public void TheLastValueMayBeSuppliedAtConstruction() {
        var tokens = new PathTokenCollection(2, ["id", "postId"], "42");

        Assert.Equal("42", tokens.Get(1).TokenValue);
    }

    [Fact]
    public void ALastValueOnAnEmptyCollectionIsIgnoredRatherThanThrowing() {
        Assert.Equal(0, new PathTokenCollection(0, [], "42").Count);
    }

    [Fact]
    public void LookupByNameFindsTheValue() {
        var tokens = new PathTokenCollection(2, ["id", "postId"]);

        tokens.SetValue(0, "7");
        tokens.SetValue(1, "42");

        Assert.Equal("42", tokens.Get("postId").ToString());
    }

    [Fact]
    public void LookupByAnUnknownNameIsEmptyRatherThanThrowing() {
        var tokens = new PathTokenCollection(1, ["id"]);

        tokens.SetValue(0, "7");

        Assert.Equal(StringValues.Empty, tokens.Get("nope"));
    }

    /// <summary>
    /// A token position that matched nothing reads as empty, not null — the callers treat the
    /// result as a string.
    /// </summary>
    [Fact]
    public void AnUnsetValueReadsAsAnEmptyStringByIndex() {
        Assert.Equal("", new PathTokenCollection(1, ["id"]).Get(0).TokenValue);
    }

    [Fact]
    public void AnUnsetValueReadsAsEmptyByName() {
        Assert.Equal(StringValues.Empty, new PathTokenCollection(1, ["id"]).Get("id"));
    }

    /// <summary>
    /// More values than the route named. <c>NameAt</c> falls back rather than indexing past the
    /// end of a shared array.
    /// </summary>
    [Fact]
    public void AValueBeyondTheSuppliedNamesHasAnEmptyName() {
        var tokens = new PathTokenCollection(2, ["id"]);

        tokens.SetValue(1, "42");

        Assert.Equal("", tokens.Get(1).TokenName);
        Assert.Equal("42", tokens.Get(1).TokenValue);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void SetValueOutsideTheRangeThrows(int index) {
        var tokens = new PathTokenCollection(2, ["id", "postId"]);

        Assert.Throws<IndexOutOfRangeException>(() => tokens.SetValue(index, "value"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void GetOutsideTheRangeThrows(int index) {
        var tokens = new PathTokenCollection(2, ["id", "postId"]);

        Assert.Throws<IndexOutOfRangeException>(() => tokens.Get(index));
    }

    [Fact]
    public void AnyIndexIntoAnEmptyCollectionThrows() {
        Assert.Throws<IndexOutOfRangeException>(() => PathTokenCollection.Empty.Get(0));
    }

    #region the legacy constructor, which owns its names

    [Fact]
    public void ACollectionThatOwnsItsNamesRecordsWhatSetIsGiven() {
        var tokens = new PathTokenCollection(2);

        tokens.Set(0, new PathToken("id", "7"));

        Assert.Equal("id", tokens.Get(0).TokenName);
        Assert.Equal("7", tokens.Get(0).TokenValue);
    }

    [Fact]
    public void TheLastTokenMayBeSuppliedAtConstruction() {
        var tokens = new PathTokenCollection(2, new PathToken("postId", "42"));

        Assert.Equal("postId", tokens.Get(1).TokenName);
        Assert.Equal("42", tokens.Get(1).TokenValue);
    }

    [Fact]
    public void ALastTokenOnAnEmptyCollectionIsIgnoredRatherThanThrowing() {
        Assert.Equal(0, new PathTokenCollection(0, new PathToken("postId", "42")).Count);
    }

    /// <summary>
    /// The guard the whole <c>_ownsNames</c> flag exists for.
    /// </summary>
    /// <remarks>
    /// The names array belongs to the matched route and is shared by every request through it.
    /// <c>Set</c> is retained for generated code that still passes a name with each value, and it
    /// must take the value and discard the name — writing it would rename the token for every
    /// concurrent request on that route.
    /// </remarks>
    [Fact]
    public void SetOnARouteSuppliedCollectionDoesNotWriteTheSharedNamesArray() {
        var routeNames = new[] { "id", "postId" };
        var tokens = new PathTokenCollection(2, routeNames);

        tokens.Set(0, new PathToken("somethingElse", "7"));

        Assert.Equal("id", routeNames[0]);
        Assert.Equal("id", tokens.Get(0).TokenName);
        Assert.Equal("7", tokens.Get(0).TokenValue);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void SetOutsideTheRangeThrows(int index) {
        var tokens = new PathTokenCollection(2);

        Assert.Throws<IndexOutOfRangeException>(() => tokens.Set(index, new PathToken("id", "7")));
    }

    #endregion
}
