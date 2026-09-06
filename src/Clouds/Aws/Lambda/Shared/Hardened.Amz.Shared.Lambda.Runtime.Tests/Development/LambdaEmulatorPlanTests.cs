using System.Text.Json;
using Hardened.Amz.Shared.Lambda.Runtime.Development;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Development;

/// <summary>
/// The decision the generated <c>Main</c> makes before it builds the bootstrap: is anything running
/// this function, and if not, what to start.
/// </summary>
public class LambdaEmulatorPlanTests {

    private static Func<string, string?> Environment(params (string Key, string Value)[] values) =>
        key => values.FirstOrDefault(v => v.Key == key).Value;

    /// <summary>
    /// The variable set means the Lambda service, the runtime interface emulator or a tool someone
    /// started by hand is already there. Starting a second one would put the function on the wrong
    /// port and leave the real caller waiting.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1:9001")]
    [InlineData("localhost:5050/Orders.Host")]
    public void NothingIsPlannedWhenSomethingAlreadyRunsTheFunction(string runtimeApi) {
        var plan = LambdaEmulatorPlan.From("Orders.Host", true,
            Environment((LambdaEmulator.RuntimeApiVariable, runtimeApi)));

        Assert.Null(plan);
    }

    [Fact]
    public void AWebApplicationGetsTheTemplatePortsAndAGateway() {
        var plan = LambdaEmulatorPlan.From("Orders.Host", true, Environment());

        Assert.NotNull(plan);
        Assert.Equal("Orders.Host", plan!.FunctionName);
        Assert.Equal(5050, plan.EmulatorPort);
        Assert.Equal(5080, plan.GatewayPort);
        Assert.Equal("localhost:5050/Orders.Host", plan.RuntimeApiEndpoint);
        Assert.Equal("http://localhost:5050", plan.EmulatorUrl);
        Assert.Equal("http://localhost:5080", plan.GatewayUrl);
        Assert.Equal(
            "lambda-test-tool start --lambda-emulator-port 5050 --no-launch-window " +
            "--api-gateway-emulator-port 5080 --api-gateway-emulator-mode HttpV2",
            plan.Arguments);
    }

    /// <summary>
    /// A function has no HTTP front door. It is invoked from the tool's UI, so the plan starts the
    /// emulator alone and there are no routes to configure.
    /// </summary>
    [Fact]
    public void AFunctionGetsTheEmulatorAlone() {
        var plan = LambdaEmulatorPlan.From("OrderQueue", false, Environment());

        Assert.NotNull(plan);
        Assert.False(plan!.ApiGateway);
        Assert.Null(plan.GatewayUrl);
        Assert.Null(plan.RouteConfiguration);
        Assert.Equal("lambda-test-tool start --lambda-emulator-port 5050 --no-launch-window", plan.Arguments);
    }

    /// <summary>
    /// <c>PORT</c> is the variable the Kestrel and ASP.NET hosts already honour, so a Lambda
    /// application moves the same way they do.
    /// </summary>
    [Fact]
    public void ThePortsComeFromTheEnvironment() {
        var plan = LambdaEmulatorPlan.From("Orders.Host", true, Environment(
            (LambdaEmulator.GatewayPortVariable, "6080"),
            (LambdaEmulator.EmulatorPortVariable, " 6050 ")));

        Assert.NotNull(plan);
        Assert.Equal(6050, plan!.EmulatorPort);
        Assert.Equal(6080, plan.GatewayPort);
        Assert.Equal("localhost:6050/Orders.Host", plan.RuntimeApiEndpoint);
        Assert.Contains("--api-gateway-emulator-port 6080", plan.Arguments);
        Assert.Contains("\"Endpoint\":\"http://localhost:6050\"", plan.RouteConfiguration);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("-1")]
    [InlineData("80.0")]
    public void AnUnusablePortFailsNamingTheVariable(string value) {
        var failure = Assert.Throws<InvalidOperationException>(() =>
            LambdaEmulatorPlan.From("Orders.Host", true, Environment((LambdaEmulator.GatewayPortVariable, value))));

        Assert.Contains(LambdaEmulator.GatewayPortVariable, failure.Message);
        Assert.Contains(value, failure.Message);
    }

    /// <summary>
    /// Hardened owns the routing table, so the gateway emulator gets two catch-all routes to the one
    /// function and every method on each. Parsed rather than compared as text, because the tool
    /// reads it as JSON and a stray quote is what would break it.
    /// </summary>
    [Fact]
    public void TheRoutesSendEveryMethodOnEveryPathToTheFunction() {
        var plan = LambdaEmulatorPlan.From("Orders.Host", true, Environment());

        using var routes = JsonDocument.Parse(plan!.RouteConfiguration!);
        var entries = routes.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, entries.Count);
        Assert.Equal(["/", "/{proxy+}"], entries.Select(e => e.GetProperty("Path").GetString()!).ToArray());
        Assert.All(entries, entry => {
            Assert.Equal("Orders.Host", entry.GetProperty("LambdaResourceName").GetString());
            Assert.Equal("ANY", entry.GetProperty("HttpMethod").GetString());
            Assert.Equal("http://localhost:5050", entry.GetProperty("Endpoint").GetString());
        });
    }
}
