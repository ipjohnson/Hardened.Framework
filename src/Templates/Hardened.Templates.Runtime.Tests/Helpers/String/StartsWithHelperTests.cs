using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String;

public class StartsWithHelperTests : BaseTwoStringTests {
    [Theory]
    [InlineData("Hello", "He", true)]
    [InlineData("Hello", "llo", false)]
    [InlineData("Hello", "", false)]
    [InlineData("", "llo", false)]
    [InlineData("", "", false)]
    public async Task StartsWithLogicTests(string one, string two, bool result) {
        await Evaluate(one, two, result);
    }

    protected override string Token => DefaultHelpers.StringHelperToken.StartsWith;

    protected override Type TemplateHelperType => typeof(StartsWithHelper);
}