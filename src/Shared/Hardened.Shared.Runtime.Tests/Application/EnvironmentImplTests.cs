using DependencyModules.Runtime.Interfaces;
using Hardened.Shared.Runtime.Application;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Application;

/// <summary>
/// <see cref="EnvironmentImpl"/> driven entirely through its dictionary seam.
///
/// <para>
/// Every environment here is given an explicit name and explicit values, so nothing in this class
/// reads or writes the host's environment. The tests that must touch the process environment live
/// in <see cref="ProcessEnvironmentTests"/>, which owns that global on its own.
/// </para>
/// </summary>
public class EnvironmentImplTests {

    private static EnvironmentImpl Environment(params (string Name, string Value)[] values) =>
        new(name: "test",
            environmentValues: values.ToDictionary(pair => pair.Name, pair => pair.Value));

    [Fact]
    public void AnExplicitNameIsTheEnvironmentName() {
        Assert.Equal("staging", new EnvironmentImpl("staging").Name);
    }

    [Fact]
    public void ArgumentsDefaultToNoneRatherThanNull() {
        Assert.Empty(new EnvironmentImpl("test").Arguments);
    }

    [Fact]
    public void ArgumentsAreKeptInTheOrderTheyWereGiven() {
        var environment = new EnvironmentImpl("test", arguments: ["--first", "--second"]);

        Assert.Equal(["--first", "--second"], environment.Arguments);
    }

    [Fact]
    public void AValueInTheDictionaryIsReturned() {
        Assert.Equal("http://localhost", Environment(("SERVICE_URL", "http://localhost")).Value<string>("SERVICE_URL"));
    }

    /// <summary>
    /// The default is returned rather than null, for a key nothing supplied. This is the branch the
    /// generated configuration read depends on: it passes the field's current value as the default.
    /// </summary>
    [Fact]
    public void AMissingValueFallsBackToTheDefault() {
        Assert.Equal("fallback", Environment().Value("SERVICE_URL", "fallback"));
    }

    [Fact]
    public void AMissingValueWithNoDefaultIsNull() {
        Assert.Null(Environment().Value<string>("SERVICE_URL"));
    }

    /// <summary>
    /// An environment constructed with no dictionary at all takes a different path from one
    /// constructed with an empty dictionary — the null-conditional lookup is skipped entirely.
    /// </summary>
    [Fact]
    public void AnEnvironmentWithNoDictionaryFallsBackToTheDefault() {
        Assert.Equal("fallback", new EnvironmentImpl("test").Value("SERVICE_URL", "fallback"));
    }

    /// <summary>
    /// An empty value is treated as absent, not as an empty string. A deployment that sets a
    /// variable to nothing gets the default rather than "".
    /// </summary>
    [Fact]
    public void AnEmptyValueIsTreatedAsAbsent() {
        Assert.Equal("fallback", Environment(("SERVICE_URL", "")).Value("SERVICE_URL", "fallback"));
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("0", 0)]
    [InlineData("-1", -1)]
    public void AnIntegerValueIsConverted(string raw, int expected) {
        Assert.Equal(expected, Environment(("RETENTION_DAYS", raw)).Value<int>("RETENTION_DAYS"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    public void ABooleanValueIsConverted(string raw, bool expected) {
        Assert.Equal(expected, Environment(("VERBOSE", raw)).Value<bool>("VERBOSE"));
    }

    [Fact]
    public void ADoubleValueIsConverted() {
        Assert.Equal(1.5, Environment(("RATIO", "1.5")).Value<double>("RATIO"));
    }

    [Fact]
    public void ALongValueIsConverted() {
        Assert.Equal(9_000_000_000L, Environment(("SIZE", "9000000000")).Value<long>("SIZE"));
    }

    /// <summary>
    /// A string is returned without going through <c>Convert.ChangeType</c> at all, which is a
    /// separate branch and the one taken by almost every real read.
    /// </summary>
    [Fact]
    public void AStringValueIsReturnedWithoutConversion() {
        Assert.Equal("not-a-number", Environment(("SERVICE_URL", "not-a-number")).Value<string>("SERVICE_URL"));
    }

    [Fact]
    public void AValueThatCannotBeConvertedThrows() {
        Assert.Throws<FormatException>(() => Environment(("RETENTION_DAYS", "ninety")).Value<int>("RETENTION_DAYS"));
    }

    [Fact]
    public void CustomDataIsReturnedWhenPresent() {
        var environment = new EnvironmentImpl("test",
            customData: new Dictionary<string, object> { ["handler"] = 42 });

        Assert.Equal(42, environment.CustomData<int>("handler"));
    }

    [Fact]
    public void MissingCustomDataFallsBackToTheDefault() {
        var environment = new EnvironmentImpl("test",
            customData: new Dictionary<string, object> { ["handler"] = 42 });

        Assert.Equal(-1, environment.CustomData("absent", -1));
    }

    /// <summary>
    /// No custom data dictionary at all is the common case, and takes a different branch from a
    /// dictionary that simply does not hold the key.
    /// </summary>
    [Fact]
    public void CustomDataWithNoDictionaryFallsBackToTheDefault() {
        Assert.Equal(-1, new EnvironmentImpl("test").CustomData("handler", -1));
    }

    [Fact]
    public void CustomDataOfTheWrongTypeThrowsRatherThanReturningTheDefault() {
        var environment = new EnvironmentImpl("test",
            customData: new Dictionary<string, object> { ["handler"] = "a string" });

        Assert.Throws<InvalidCastException>(() => environment.CustomData<int>("handler"));
    }

    /// <summary>
    /// <c>IModuleEnvironment</c> is what DependencyModules registers against, and
    /// <see cref="IHardenedEnvironment"/> satisfies it with default interface implementations. If
    /// they stopped forwarding, environment-scoped service registration would silently see the wrong
    /// environment.
    /// </summary>
    [Fact]
    public void TheModuleEnvironmentNameIsTheHardenedEnvironmentName() {
        IModuleEnvironment environment = new EnvironmentImpl("staging");

        Assert.Equal("staging", environment.EnvironmentName);
    }

    [Fact]
    public void TheModuleEnvironmentValueIsTheHardenedEnvironmentValue() {
        IModuleEnvironment environment = Environment(("SERVICE_URL", "http://localhost"));

        Assert.Equal("http://localhost", environment.Value("SERVICE_URL"));
    }

    [Fact]
    public void TheModuleEnvironmentValueIsNullForAnUnsetVariable() {
        IModuleEnvironment environment = Environment();

        Assert.Null(environment.Value("SERVICE_URL"));
    }
}

/// <summary>
/// <c>Matches</c> and <c>MatchesVariable</c> — how a module asks "am I in production?" without
/// string-comparing the name itself everywhere.
/// </summary>
public class EnvironmentMatchingTests {

    [Theory]
    [InlineData("production", "production", true)]
    [InlineData("production", "Production", true)]
    [InlineData("Production", "production", true)]
    [InlineData("PRODUCTION", "production", true)]
    [InlineData("production", "development", false)]
    [InlineData("prod", "production", false)]
    public void MatchesComparesTheEnvironmentNameWithoutCase(string name, string candidate, bool expected) {
        Assert.Equal(expected, new EnvironmentImpl(name).Matches(candidate));
    }

    [Fact]
    public void MatchesIsTrueWhenAnyCandidateMatches() {
        Assert.True(new EnvironmentImpl("staging").Matches("production", "staging"));
    }

    [Fact]
    public void MatchesIsFalseWhenNoCandidateMatches() {
        Assert.False(new EnvironmentImpl("staging").Matches("production", "development"));
    }

    /// <summary>Asking whether the environment is one of nothing is false, not an error.</summary>
    [Fact]
    public void MatchesWithNoCandidatesIsFalse() {
        Assert.False(new EnvironmentImpl("staging").Matches());
    }

    [Theory]
    [InlineData("on", "on", true)]
    [InlineData("ON", "on", true)]
    [InlineData("on", "ON", true)]
    [InlineData("on", "off", false)]
    public void MatchesVariableComparesTheValueWithoutCase(string actual, string expected, bool matches) {
        var environment = new EnvironmentImpl("test",
            environmentValues: new Dictionary<string, string> { ["FEATURE"] = actual });

        Assert.Equal(matches, environment.MatchesVariable("FEATURE", expected));
    }

    /// <summary>
    /// An unset variable reads as the empty string rather than null, so <c>MatchesVariable</c>
    /// answers false instead of throwing. A feature flag nobody set is off.
    /// </summary>
    [Fact]
    public void MatchesVariableIsFalseForAnUnsetVariable() {
        Assert.False(new EnvironmentImpl("test").MatchesVariable("FEATURE", "on"));
    }

    [Fact]
    public void MatchesVariableIsTrueWhenTheExpectedValueIsAlsoEmpty() {
        Assert.True(new EnvironmentImpl("test").MatchesVariable("FEATURE", ""));
    }
}
