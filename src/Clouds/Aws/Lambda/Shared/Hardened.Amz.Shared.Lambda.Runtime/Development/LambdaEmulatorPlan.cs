using System.Globalization;
using System.Text.Json;

namespace Hardened.Amz.Shared.Lambda.Runtime.Development;

/// <summary>
/// What <see cref="LambdaEmulator"/> would start, worked out from the environment and nothing
/// else, so the decision can be tested without a process.
/// </summary>
/// <remarks>
/// <para>
/// There is no plan when the function is not local. <c>AWS_LAMBDA_RUNTIME_API</c> set means
/// something is already running the function - the Lambda service, the runtime interface emulator
/// in a container, a test tool started by hand - and the bootstrap goes where that says.
/// </para>
/// <para>
/// The ports are the ones the templates print and document: the gateway on <c>PORT</c>, 5080 by
/// default, so a Lambda application answers on the same address the Kestrel host does; the
/// emulator itself, which is also the tool's web UI, on 5050, the tool's own default.
/// </para>
/// </remarks>
public sealed class LambdaEmulatorPlan {
    /// <summary>
    /// The variable the API Gateway emulator reads its routes from. Set on the tool's process, not
    /// the function's.
    /// </summary>
    public const string RouteConfigurationVariable = "APIGATEWAY_EMULATOR_ROUTE_CONFIG";

    private LambdaEmulatorPlan(string functionName, bool apiGateway, int emulatorPort, int gatewayPort) {
        FunctionName = functionName;
        ApiGateway = apiGateway;
        EmulatorPort = emulatorPort;
        GatewayPort = gatewayPort;

        Arguments =
            $"lambda-test-tool start --lambda-emulator-port {emulatorPort} --no-launch-window" +
            (apiGateway
                ? $" --api-gateway-emulator-port {gatewayPort} --api-gateway-emulator-mode HttpV2"
                : "");

        RouteConfiguration = apiGateway ? Routes(functionName, EmulatorUrl) : null;
    }

    /// <summary>
    /// The name the function registers under, and the last segment of
    /// <see cref="RuntimeApiEndpoint"/>. The assembly name, which is also what
    /// <c>Hardened.Amz.Cdk</c> deploys as the handler.
    /// </summary>
    public string FunctionName { get; }

    /// <summary>
    /// Whether an API Gateway emulator fronts the function: true for a web application, false for
    /// a function, which is invoked from the tool's UI or an event source instead.
    /// </summary>
    public bool ApiGateway { get; }

    public int EmulatorPort { get; }

    public int GatewayPort { get; }

    /// <summary>
    /// Where the bootstrap polls for invocations: host and port with the function name as the
    /// path, and no scheme. That is the form the tool documents, and the bootstrap prefixes the
    /// scheme itself.
    /// </summary>
    public string RuntimeApiEndpoint => $"localhost:{EmulatorPort}/{FunctionName}";

    /// <summary>The tool's web UI, where a function without a gateway is invoked from.</summary>
    public string EmulatorUrl => $"http://localhost:{EmulatorPort}";

    /// <summary>The address the application answers on. Null without a gateway.</summary>
    public string? GatewayUrl => ApiGateway ? $"http://localhost:{GatewayPort}" : null;

    /// <summary>The arguments to <c>dotnet</c>.</summary>
    public string Arguments { get; }

    /// <summary>
    /// Every method on every path, to this one function. Null without a gateway.
    /// </summary>
    public string? RouteConfiguration { get; }

    /// <summary>
    /// The plan for a function, or null when the function is not local.
    /// </summary>
    /// <param name="functionName">See <see cref="FunctionName"/>.</param>
    /// <param name="apiGateway">See <see cref="ApiGateway"/>.</param>
    /// <param name="environment">
    /// The environment, as a lookup rather than <see cref="Environment.GetEnvironmentVariable(string)"/>
    /// so a test can supply one.
    /// </param>
    public static LambdaEmulatorPlan? From(string functionName, bool apiGateway, Func<string, string?> environment) {
        if (!string.IsNullOrWhiteSpace(environment(LambdaEmulator.RuntimeApiVariable))) {
            return null;
        }

        return new LambdaEmulatorPlan(
            functionName,
            apiGateway,
            Port(environment, LambdaEmulator.EmulatorPortVariable, LambdaEmulator.DefaultEmulatorPort),
            Port(environment, LambdaEmulator.GatewayPortVariable, LambdaEmulator.DefaultGatewayPort));
    }

    private static int Port(Func<string, string?> environment, string variable, int fallback) {
        var value = environment(variable);

        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }

        if (int.TryParse(value!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
            port is > 0 and <= 65535) {
            return port;
        }

        throw new InvalidOperationException(
            $"{variable} is '{value}'. It must be a port number between 1 and 65535.");
    }

    // Written by hand rather than serialised: two fixed entries, and a serialiser here would be the
    // only reflection-based one in the assembly.
    private static string Routes(string functionName, string endpoint) {
        var name = JsonEncodedText.Encode(functionName).ToString();

        return
            "[" +
            $"{{\"LambdaResourceName\":\"{name}\",\"Endpoint\":\"{endpoint}\",\"HttpMethod\":\"ANY\",\"Path\":\"/\"}}," +
            $"{{\"LambdaResourceName\":\"{name}\",\"Endpoint\":\"{endpoint}\",\"HttpMethod\":\"ANY\",\"Path\":\"/{{proxy+}}\"}}" +
            "]";
    }
}
