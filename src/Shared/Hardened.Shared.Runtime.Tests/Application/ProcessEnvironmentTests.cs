using Hardened.Shared.Runtime.Application;
using Xunit;

namespace Hardened.Shared.Runtime.Tests.Application;

/// <summary>
/// The two places <see cref="EnvironmentImpl"/> reaches past its dictionary into the host: the
/// <c>HARDENED_ENVIRONMENT</c> variable that names an unnamed environment, and the fallback read in
/// <c>Value&lt;T&gt;</c>.
///
/// <para>
/// A process environment variable is global to the test host, so these are deliberately gathered
/// into one class. xUnit runs the tests within a class one at a time, which is what stops a test
/// asserting the <c>development</c> default from running while another has <c>HARDENED_ENVIRONMENT</c>
/// set. Nothing else in this assembly constructs an <see cref="EnvironmentImpl"/> without a name or
/// reads a variable it did not put in a dictionary, so no other class can observe these.
/// </para>
///
/// <para>Every variable is restored in a <c>finally</c>, including when it was previously unset.</para>
/// </summary>
public class ProcessEnvironmentTests {

    private static void WithProcessVariable(string name, string? value, Action body) {
        var previous = Environment.GetEnvironmentVariable(name);

        Environment.SetEnvironmentVariable(name, value);

        try {
            body();
        }
        finally {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    /// <summary>
    /// Documented behaviour: an environment given no name takes it from <c>HARDENED_ENVIRONMENT</c>.
    /// This is how a deployed process knows which environment it is in.
    /// </summary>
    [Fact]
    public void AnUnnamedEnvironmentTakesItsNameFromHardenedEnvironment() {
        WithProcessVariable("HARDENED_ENVIRONMENT", "production",
            () => Assert.Equal("production", new EnvironmentImpl().Name));
    }

    /// <summary>
    /// An explicit name wins. A test host that constructs its own environment must not be overridden
    /// by whatever the developer's shell happens to export.
    /// </summary>
    [Fact]
    public void AnExplicitNameWinsOverHardenedEnvironment() {
        WithProcessVariable("HARDENED_ENVIRONMENT", "production",
            () => Assert.Equal("staging", new EnvironmentImpl("staging").Name));
    }

    /// <summary>
    /// Documented default: with no name and no variable, the environment is <c>development</c> —
    /// the safe end of the range to default to.
    /// </summary>
    [Fact]
    public void TheDefaultEnvironmentIsDevelopment() {
        WithProcessVariable("HARDENED_ENVIRONMENT", null,
            () => Assert.Equal("development", new EnvironmentImpl().Name));
    }

    /// <summary>
    /// A variable nothing put in the dictionary is read from the process, which is how a Lambda
    /// picks up the variables its function configuration sets.
    /// </summary>
    [Fact]
    public void AVariableNotInTheDictionaryIsReadFromTheProcess() {
        WithProcessVariable("HARDENED_TEST_SERVICE_URL", "http://from-process",
            () => Assert.Equal(
                "http://from-process",
                new EnvironmentImpl("test").Value<string>("HARDENED_TEST_SERVICE_URL")));
    }

    /// <summary>
    /// The dictionary wins over the process. A test host seeding an environment must not be
    /// silently overridden by a variable the developer exported.
    /// </summary>
    [Fact]
    public void ADictionaryValueWinsOverTheProcessVariable() {
        WithProcessVariable("HARDENED_TEST_SERVICE_URL", "http://from-process", () => {
            var environment = new EnvironmentImpl("test",
                environmentValues: new Dictionary<string, string> {
                    ["HARDENED_TEST_SERVICE_URL"] = "http://from-dictionary"
                });

            Assert.Equal("http://from-dictionary", environment.Value<string>("HARDENED_TEST_SERVICE_URL"));
        });
    }

    /// <summary>
    /// An empty dictionary entry is not a value, so the process variable is consulted rather than
    /// the empty string being taken as an answer.
    /// </summary>
    [Fact]
    public void AnEmptyDictionaryValueFallsThroughToTheProcessVariable() {
        WithProcessVariable("HARDENED_TEST_SERVICE_URL", "http://from-process", () => {
            var environment = new EnvironmentImpl("test",
                environmentValues: new Dictionary<string, string> { ["HARDENED_TEST_SERVICE_URL"] = "" });

            Assert.Equal("http://from-process", environment.Value<string>("HARDENED_TEST_SERVICE_URL"));
        });
    }

    /// <summary>A value read from the process is converted like any other.</summary>
    [Fact]
    public void AProcessVariableIsConvertedToTheRequestedType() {
        WithProcessVariable("HARDENED_TEST_RETENTION_DAYS", "90",
            () => Assert.Equal(90, new EnvironmentImpl("test").Value<int>("HARDENED_TEST_RETENTION_DAYS")));
    }

    /// <summary>
    /// With neither a dictionary entry nor a process variable, the default is returned — the case
    /// every optional setting takes.
    /// </summary>
    [Fact]
    public void AVariableSetNowhereFallsBackToTheDefault() {
        WithProcessVariable("HARDENED_TEST_SERVICE_URL", null,
            () => Assert.Equal(
                "fallback",
                new EnvironmentImpl("test").Value("HARDENED_TEST_SERVICE_URL", "fallback")));
    }
}
