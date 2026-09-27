using Hardened.Shared.Testing.Impl;

namespace Hardened.Shared.Testing.Tests.Impl;

/// <summary>
/// A test's environment reads the values a test declares with the conversion the application's
/// environment uses, so a test sees the failure a deployment would.
/// </summary>
public class TestEnvironmentTests
{
    private static TestEnvironment Environment(string name, object value) =>
        new("test", new Dictionary<string, object> { [name] = value });

    [Fact]
    public void AValueOfTheTypeAskedForIsReturnedAsItIs()
    {
        Assert.Equal(90, Environment("RETENTION_DAYS", 90).Value<int>("RETENTION_DAYS"));
    }

    [Fact]
    public void TextIsConvertedToTheTypeAskedFor()
    {
        Assert.Equal(90, Environment("RETENTION_DAYS", "90").Value<int?>("RETENTION_DAYS"));
    }

    [Fact]
    public void AValueThatCannotBeConvertedThrowsNamingTheVariable()
    {
        var exception = Assert.Throws<FormatException>(() =>
            Environment("FLEET_LOW_FUEL_PERCENT", "fifteen").Value<int>("FLEET_LOW_FUEL_PERCENT")
        );

        Assert.StartsWith(
            "The environment variable FLEET_LOW_FUEL_PERCENT could not be read as Int32: ",
            exception.Message
        );
    }
}
