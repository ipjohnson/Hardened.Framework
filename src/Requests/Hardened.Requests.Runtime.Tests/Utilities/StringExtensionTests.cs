using Hardened.Requests.Abstract.Utilities;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Utilities;

public class StringExtensionTests {
    [Fact]
    public void SingleStringToStringValues() {
        var stringValues = "simple".ToStringValues();

        Assert.Single(stringValues);
        Assert.Equal("simple", stringValues[0]);
    }

    [Fact]
    public void TwoStringToStringValues() {
        var stringValues = "first,second".ToStringValues();

        Assert.Equal(2, stringValues.Count);
        Assert.Equal("first", stringValues[0]);
        Assert.Equal("second", stringValues[1]);
    }

    [Fact]
    public void TwoStringWithSpaceToStringValues() {
        var stringValues = "first, second".ToStringValues();

        Assert.Equal(2, stringValues.Count);
        Assert.Equal("first", stringValues[0]);
        Assert.Equal("second", stringValues[1]);
    }


    [Fact]
    public void EmptyStringsToStringValues() {
        var stringValues = ", second,".ToStringValues();

        Assert.Equal(3, stringValues.Count);
        Assert.Equal("", stringValues[0]);
        Assert.Equal("second", stringValues[1]);
        Assert.Equal("", stringValues[2]);
    }
}