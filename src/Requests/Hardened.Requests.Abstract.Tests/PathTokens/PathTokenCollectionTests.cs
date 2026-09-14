using Hardened.Requests.Abstract.PathTokens;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Hardened.Requests.Abstract.Tests.PathTokens;

/// <summary>
/// Path token values for a matched route.
/// </summary>
/// <remarks>
/// The names array belongs to the route and is shared by every request that matches it, so nothing
/// here writes to it, and its length is the token count. Values are positional, and a route with
/// one token holds its value in the field rather than in an array - see
/// <see cref="SeveralTokensReadBackTheSameAsOne"/> for the shape that does allocate.
/// </remarks>
public class PathTokenCollectionTests
{
    [Fact]
    public void EmptyHasNoTokens()
    {
        Assert.Equal(0, PathTokenCollection.Empty.Count);
    }

    [Fact]
    public void CountIsTheNumberOfNamesTheRouteDeclared()
    {
        Assert.Equal(3, new PathTokenCollection(["a", "b", "c"]).Count);
    }

    [Fact]
    public void AValueSetPositionallyReadsBackUnderTheRoutesName()
    {
        var tokens = new PathTokenCollection(["id", "postId"]);

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
    public void TheLastValueMayBeSuppliedAtConstruction()
    {
        Assert.Equal("42", new PathTokenCollection(["id", "postId"], "42").Get(1).TokenValue);
        Assert.Equal("7", new PathTokenCollection(["id"], "7").Get(0).TokenValue);
    }

    [Fact]
    public void ALastValueOnAnEmptyCollectionIsIgnoredRatherThanThrowing()
    {
        Assert.Equal(0, new PathTokenCollection([], "42").Count);
    }

    /// <summary>
    /// One token is held in the field and more than one in an array, so the two paths are asserted
    /// to read back identically.
    /// </summary>
    [Fact]
    public void SeveralTokensReadBackTheSameAsOne()
    {
        var one = new PathTokenCollection(["a"]);
        var several = new PathTokenCollection(["a", "b", "c"]);

        one.SetValue(0, "1");
        several.SetValue(0, "1");
        several.SetValue(1, "2");
        several.SetValue(2, "3");

        Assert.Equal("1", one.Get("a").ToString());
        Assert.Equal("1", several.Get("a").ToString());
        Assert.Equal("2", several.Get("b").ToString());
        Assert.Equal("3", several.Get("c").ToString());
    }

    [Fact]
    public void LookupByAnUnknownNameIsEmptyRatherThanThrowing()
    {
        var tokens = new PathTokenCollection(["id"]);

        tokens.SetValue(0, "7");

        Assert.Equal(StringValues.Empty, tokens.Get("nope"));
        Assert.Equal(StringValues.Empty, PathTokenCollection.Empty.Get("id"));
    }

    /// <summary>
    /// A token position that matched nothing reads as empty, not null - the callers treat the
    /// result as a string.
    /// </summary>
    [Fact]
    public void AnUnsetValueReadsAsAnEmptyStringByIndex()
    {
        Assert.Equal("", new PathTokenCollection(["id"]).Get(0).TokenValue);
    }

    [Fact]
    public void AnUnsetValueReadsAsEmptyByName()
    {
        Assert.Equal(StringValues.Empty, new PathTokenCollection(["id"]).Get("id"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void SetValueOutsideTheRangeThrows(int index)
    {
        var tokens = new PathTokenCollection(["id", "postId"]);

        Assert.Throws<IndexOutOfRangeException>(() => tokens.SetValue(index, "value"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void GetOutsideTheRangeThrows(int index)
    {
        var tokens = new PathTokenCollection(["id", "postId"]);

        Assert.Throws<IndexOutOfRangeException>(() => tokens.Get(index));
    }

    [Fact]
    public void AnyIndexIntoAnEmptyCollectionThrows()
    {
        Assert.Throws<IndexOutOfRangeException>(() => PathTokenCollection.Empty.Get(0));
    }

    /// <summary>
    /// The routing table writes through a <c>ref</c> parameter, so every node up the unwind is
    /// writing into one collection rather than into copies of one.
    /// </summary>
    [Fact]
    public void WritingThroughARefParameterIsSeenByTheOwner()
    {
        var tokens = new PathTokenCollection(["id", "postId"], "42");

        SetFirst(ref tokens, "7");

        Assert.Equal("7", tokens.Get("id").ToString());
        Assert.Equal("42", tokens.Get("postId").ToString());
    }

    private static void SetFirst(ref PathTokenCollection tokens, string value) =>
        tokens.SetValue(0, value);
}
