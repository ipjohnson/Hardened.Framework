using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.Url;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.Url;

public class EncodeHelperTests : BaseHelperTests {
    [Fact]
    public async Task EncodeUrlTest() {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), "https://www.google.com/");

        Assert.NotNull(result);
        Assert.Equal("https%3a%2f%2fwww.google.com%2f", result);
    }

    protected override Type TemplateHelperType => typeof(EncodeHelper);

    protected override string Token => DefaultHelpers.UrlHelperTokens.Encode;
}