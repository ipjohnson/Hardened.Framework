using Hardened.Amz.Web.Lambda.Runtime.Logging;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// The log level a Lambda web application boots with.
///
/// <para>
/// It is decided once, at construction, from the environment. On a deployed function the only lever
/// left is an environment variable, which is why <c>LOG_LEVEL</c> has to win over the default the
/// environment name implies.
/// </para>
/// </summary>
public class LambdaWebLoggerHelperTests {

    private const string EntryNamespace = "TestApp";

    private static LogLevel LevelFor(string environmentName, string? logLevel = null) {
        var values = new Dictionary<string, string>();

        if (logLevel != null) {
            values["LOG_LEVEL"] = logLevel;
        }

        return LevelOf(
            LambdaWebLoggerHelper.CreateAction(
                new EnvironmentImpl(environmentName, values), EntryNamespace),
            EntryNamespace);
    }

    /// <summary>
    /// The helper's only observable output is the filter rules it installs, so they are read back
    /// off a real <see cref="LoggerFilterOptions"/> rather than a stand-in for the builder.
    /// </summary>
    private static LogLevel LevelOf(Action<ILoggingBuilder> action, string category) {
        var services = new ServiceCollection();

        services.AddLogging(action);

        using var provider = services.BuildServiceProvider();

        var rule = provider
            .GetRequiredService<IOptions<LoggerFilterOptions>>()
            .Value.Rules
            .Last(candidate => candidate.CategoryName == category);

        Assert.NotNull(rule.LogLevel);

        return rule.LogLevel.Value;
    }

    [Fact]
    public void ProductionLogsAtInformation() {
        Assert.Equal(LogLevel.Information, LevelFor("production"));
    }

    [Fact]
    public void DevelopmentLogsAtDebug() {
        Assert.Equal(LogLevel.Debug, LevelFor("development"));
    }

    [Fact]
    public void TestLogsAtDebug() {
        Assert.Equal(LogLevel.Debug, LevelFor("test"));
    }

    /// <summary>
    /// The variable is the only lever available on a deployed function, so it has to beat the
    /// default the environment name implies — in both directions.
    /// </summary>
    [Theory]
    [InlineData("production", "Trace", LogLevel.Trace)]
    [InlineData("production", "Warning", LogLevel.Warning)]
    [InlineData("development", "Error", LogLevel.Error)]
    public void TheLogLevelVariableWinsOverTheEnvironmentDefault(
        string environmentName, string logLevel, LogLevel expected) {
        Assert.Equal(expected, LevelFor(environmentName, logLevel));
    }

    /// <summary>
    /// A typo in <c>LOG_LEVEL</c> leaves the default in place rather than throwing during
    /// construction, which on Lambda is a cold-start failure with no logs to explain it.
    /// </summary>
    [Fact]
    public void AnUnparseableLogLevelFallsBackToTheEnvironmentDefault() {
        Assert.Equal(LogLevel.Information, LevelFor("production", "chatty"));
    }

    [Fact]
    public void AnExplicitLevelIsUsedAsGiven() {
        Assert.Equal(LogLevel.Critical,
            LevelOf(LambdaWebLoggerHelper.CreateAction(LogLevel.Critical, EntryNamespace),
                EntryNamespace));
    }

    /// <summary>
    /// Framework logging stays at warning whatever the application level is, or a debug build
    /// drowns in ASP.NET and AWS SDK chatter that costs money to store.
    /// </summary>
    [Theory]
    [InlineData("Microsoft")]
    [InlineData("System")]
    public void FrameworkNamespacesStayAtWarning(string category) {
        Assert.Equal(LogLevel.Warning,
            LevelOf(LambdaWebLoggerHelper.CreateAction(LogLevel.Trace, EntryNamespace), category));
    }

    [Fact]
    public void TheHardenedNamespaceFollowsTheApplicationLevel() {
        Assert.Equal(LogLevel.Trace,
            LevelOf(LambdaWebLoggerHelper.CreateAction(LogLevel.Trace, EntryNamespace), "Hardened"));
    }
}
