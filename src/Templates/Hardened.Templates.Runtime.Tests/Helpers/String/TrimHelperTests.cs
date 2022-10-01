using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String;

public class TrimHelperTests : BaseSingleStringTests
{
    [Theory]
    [InlineData(" Hello", "Hello")]
    [InlineData("Hello ", "Hello")]
    [InlineData("Hello", "Hello")]
    public async void TrimLogicTests(string input, string expected)
    {
        await Evaluate(input, expected);
    }

    protected override Type TemplateHelperType => typeof(TrimHelper);

    protected override string Token => DefaultHelpers.StringHelperToken.Trim;
}