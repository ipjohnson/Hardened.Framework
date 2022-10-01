using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String;

public class ToLowerTests : BaseSingleStringTests
{
    [Theory]
    [InlineData("Hello", "hello")]
    [InlineData("HELLO WORLD", "hello world")]
    public async void ToLowerLogicTests(string input, string expected)
    {
        await Evaluate(input, expected);
    }

    protected override Type TemplateHelperType => typeof(ToLowerHelper);

    protected override string Token => DefaultHelpers.StringHelperToken.ToLower;
}