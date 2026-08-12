using Hardened.Shared.Runtime.Collections;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Collections;

public class CollectionExtensionsTests {

    [Fact]
    public void ForeachVisitsEveryValueInOrder() {
        var visited = new List<int>();

        new[] { 1, 2, 3 }.Foreach(visited.Add);

        Assert.Equal([1, 2, 3], visited);
    }

    [Fact]
    public void ForeachOverNothingDoesNothing() {
        var visited = 0;

        Array.Empty<int>().Foreach(_ => visited++);

        Assert.Equal(0, visited);
    }

    [Fact]
    public void GetOrDefaultReturnsTheValueForAKeyThatIsPresent() {
        var dictionary = new Dictionary<string, int> { ["present"] = 7 };

        Assert.Equal(7, dictionary.GetOrDefault("present"));
    }

    [Fact]
    public void GetOrDefaultReturnsTheGivenDefaultForAMissingKey() {
        var dictionary = new Dictionary<string, int> { ["present"] = 7 };

        Assert.Equal(-1, dictionary.GetOrDefault("absent", -1));
    }

    /// <summary>
    /// With no default given, a missing key reads as the type's own default rather than throwing.
    /// </summary>
    [Fact]
    public void GetOrDefaultWithNoDefaultReturnsTheTypesDefault() {
        Assert.Equal(0, new Dictionary<string, int>().GetOrDefault("absent"));
        Assert.Null(new Dictionary<string, string?>().GetOrDefault("absent"));
    }

    /// <summary>
    /// A key present with a value equal to the default is still a hit. A lookup that cannot tell
    /// "absent" from "present and false" makes every flag unreadable.
    /// </summary>
    [Fact]
    public void GetOrDefaultDistinguishesAPresentDefaultValueFromAMissingKey() {
        var dictionary = new Dictionary<string, bool> { ["explicitlyFalse"] = false };

        Assert.False(dictionary.GetOrDefault("explicitlyFalse", true));
        Assert.True(dictionary.GetOrDefault("absent", true));
    }
}
