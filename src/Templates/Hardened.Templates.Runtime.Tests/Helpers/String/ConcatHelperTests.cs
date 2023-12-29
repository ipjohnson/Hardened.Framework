using Hardened.Templates.Runtime.Helpers;
using Hardened.Templates.Runtime.Helpers.String;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Helpers.String;

public class ConcatHelperTests : BaseHelperTests {
    [Fact]
    public async Task ConcatArrayOfInt() {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), 1, 2, 3, 4, 5);

        Assert.NotNull(result);
        Assert.Equal("12345", result);
    }

    [Fact]
    public async Task ConcatArrayOfString() {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), "1", "2", "3", "4", "5");

        Assert.NotNull(result);
        Assert.Equal("12345", result);
    }

    [Fact]
    public async Task ConcatListOfInt() {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), new List<int> {
            1,
            2,
            3,
            4,
            5
        });

        Assert.NotNull(result);
        Assert.Equal("12345", result);
    }

    [Fact]
    public async Task ConcatListOfString() {
        var helper = GetHelper();

        var result = await helper.Execute(GetExecutionContext(), new List<string> {
            "1",
            "2",
            "3",
            "4",
            "5"
        });

        Assert.NotNull(result);
        Assert.Equal("12345", result);
    }

    protected override Type TemplateHelperType => typeof(ConcatHelper);

    protected override string Token => DefaultHelpers.StringHelperToken.Concat;
}