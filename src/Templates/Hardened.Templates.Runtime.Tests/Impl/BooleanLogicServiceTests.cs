using Hardened.Templates.Runtime.Impl;
using Xunit;

namespace Hardened.Templates.Runtime.Tests.Impl;

public class BooleanLogicServiceTests {
    [Theory]
    [InlineData(null, false)]
    [InlineData("Hello", true)]
    [InlineData("", false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void TestValues(object testValue, bool result) {
        var booleanLogicService = new BooleanLogicService();

        Assert.Equal(result, booleanLogicService.IsTrueValue(testValue));
    }
}