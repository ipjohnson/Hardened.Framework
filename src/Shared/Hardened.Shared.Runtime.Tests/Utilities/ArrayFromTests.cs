using Hardened.Shared.Runtime.Utilities;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Utilities;

/// <summary>
/// The helper generated code uses to build a metadata array without writing an array initializer.
/// </summary>
/// <remarks>
/// Trivial, and it ships — every handler's emitted metadata goes through it. What is worth pinning
/// is that <c>ObjectValues</c> hands back the params array itself rather than a copy, because the
/// generated call site is <c>ArrayFrom.ObjectValues(a, b, c)</c> and an extra copy per handler
/// construction is exactly the allocation the helper exists to avoid.
/// </remarks>
public class ArrayFromTests {

    [Fact]
    public void ObjectValuesReturnsTheValuesInOrder() {
        var values = ArrayFrom.ObjectValues("first", 2, true);

        Assert.Equal(["first", 2, true], values);
    }

    [Fact]
    public void ObjectValuesOfNothingIsEmptyRatherThanNull() {
        Assert.Empty(ArrayFrom.ObjectValues());
    }

    [Fact]
    public void ObjectValuesHandsBackTheParamsArrayItself() {
        var source = new object[] { "first", 2 };

        Assert.Same(source, ArrayFrom.ObjectValues(source));
    }

    [Fact]
    public void ValuesKeepsTheElementType() {
        var values = ArrayFrom.Values("first", "second");

        Assert.IsType<string[]>(values);
        Assert.Equal(["first", "second"], values);
    }

    [Fact]
    public void ValuesOfNothingIsEmptyRatherThanNull() {
        Assert.Empty(ArrayFrom.Values<string>());
    }
}
