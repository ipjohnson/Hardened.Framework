using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String;

public class AppendHelperTests : BaseHelperTests
{
    [Fact]
    public async Task AppendString()
    {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), "string", "-append");

        Assert.NotNull(result);
        Assert.Equal("string-append", result);
    }

    [Fact]
    public async Task AppendInt()
    {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), "string", 1);

        Assert.NotNull(result);
        Assert.Equal("string1", result);
    }

    [Fact]
    public async Task AppendNull()
    {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), "string");

        Assert.NotNull(result);
        Assert.Equal("string", result);
    }

    [Fact]
    public async Task NullString()
    {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), Array.Empty<object>());

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result);
    }
    protected override Type TemplateHelperType => typeof(AppendHelper);

    protected override string Token => DefaultHelpers.StringHelperToken.Append;
}