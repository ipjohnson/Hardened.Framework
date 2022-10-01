using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String;

public class ToUpperTests : BaseSingleStringTests
{
    [Theory]
    [InlineData("Hello", "HELLO")]
    [InlineData("hello world","HELLO WORLD")]
    public async void ToUpperLogicTests(string input, string expected)
    {
        await Evaluate(input, expected);
    }

    protected override Type TemplateHelperType => typeof(ToUpperHelper);

    protected override string Token => DefaultHelpers.StringHelperToken.ToUpper;
}