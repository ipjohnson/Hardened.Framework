using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Hardened.Aws.Lambda.Runtime.Development;

/// <summary>
/// Runs the function against the AWS Lambda Test Tool when nothing else is running it.
/// </summary>
/// <remarks>
/// <para>
/// A deployed function is started by the Lambda service, which tells the bootstrap where the
/// Runtime API is through <c>AWS_LAMBDA_RUNTIME_API</c>. Started from an IDE or <c>dotnet run</c>
/// there is no such address, so the bootstrap fails on the first poll with a URI it cannot parse -
/// which is a poor answer to pressing F5.
/// </para>
/// <para>
/// The test tool is AWS's emulator of the Runtime API, with an API Gateway emulator beside it.
/// A function's entry point asks here for a session before it starts the bootstrap. On Lambda, or
/// under anything else that sets the variable, the session is passive and the bootstrap reads the
/// address as it always did. Otherwise the tool is started as a child process, or reused when one
/// is already listening, and the bootstrap is pointed at it. F5 in any IDE, no second project, and
/// the debugger is on the process the Lambda service would start.
/// </para>
/// <para>
/// The tool is a <c>dotnet</c> tool. The templates pin it in <c>.config/dotnet-tools.json</c>, so
/// <c>dotnet lambda-test-tool</c> resolves from any directory under the solution once
/// <c>dotnet tool restore</c> has run. A global install resolves too. The tool is started from
/// the application's own directory for that reason: <c>dotnet</c> finds a manifest by walking up.
/// </para>
/// </remarks>
public static class LambdaEmulator {
    public const string RuntimeApiVariable = "AWS_LAMBDA_RUNTIME_API";

    public const string EmulatorPortVariable = "HARDENED_LAMBDA_EMULATOR_PORT";

    /// <summary>The same variable the Kestrel and ASP.NET hosts listen on.</summary>
    public const string GatewayPortVariable = "PORT";

    public const int DefaultEmulatorPort = 5050;

    public const int DefaultGatewayPort = 5080;

    // Generous, because the first start on a machine JIT-compiles the tool and its ASP.NET Core
    // host. A tool that is not installed exits at once and is reported as such, not by this.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The session a function's entry point starts its bootstrap under.
    /// </summary>
    /// <param name="applicationType">
    /// The application class. Its assembly name is the function name, which is also the handler a
    /// deployment registers.
    /// </param>
    /// <param name="apiGateway">
    /// True for a web application, which is fronted by the tool's API Gateway emulator. False for a
    /// function, which is invoked from the tool's UI or an event source.
    /// </param>
    /// <remarks>
    /// <b>It sets <c>AWS_LAMBDA_RUNTIME_API</c> on this process, and that is the whole of the
    /// wiring.</b> The Lambda service sets it for a deployed function, so an emulator standing in
    /// for the service should present the same interface - which leaves the bootstrap reading the
    /// environment exactly as it does in production, with no local branch anywhere in the host.
    /// </remarks>
    public static Task<LambdaEmulatorSession> StartIfLocal(Type applicationType, bool apiGateway = false) {
        var functionName = applicationType.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"'{applicationType}' is in an assembly with no name, so there is no function name to register under.");

        var plan = LambdaEmulatorPlan.From(functionName, apiGateway, Environment.GetEnvironmentVariable);

        return plan == null ? Task.FromResult(LambdaEmulatorSession.Passive) : Start(plan);
    }

    /// <summary>
    /// Starts the tool a plan describes, or attaches to one already listening on its port.
    /// </summary>
    /// <remarks>
    /// Reuse is what makes the debugger's stop button harmless. It kills this process and not the
    /// tool, so the next start finds the tool listening with the same routes and carries on.
    /// </remarks>
    public static async Task<LambdaEmulatorSession> Start(LambdaEmulatorPlan plan) {
        if (await IsListening(plan.EmulatorPort)) {
            Point(plan);
            Announce(plan, started: false);

            return new LambdaEmulatorSession(plan, null);
        }

        var startInfo = new ProcessStartInfo("dotnet", plan.Arguments) {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory
        };

        if (plan.RouteConfiguration != null) {
            startInfo.Environment[LambdaEmulatorPlan.RouteConfigurationVariable] = plan.RouteConfiguration;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("dotnet did not start, so the Lambda Test Tool could not be started.");

        var session = new LambdaEmulatorSession(plan, process);

        try {
            await WaitUntilListening(plan, process);
        }
        catch {
            session.Dispose();

            throw;
        }

        Point(plan);
        Announce(plan, started: true);

        return session;
    }

    private static async Task WaitUntilListening(LambdaEmulatorPlan plan, Process process) {
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (true) {
            if (process.HasExited) {
                throw new InvalidOperationException(
                    $"The AWS Lambda Test Tool exited with code {process.ExitCode} before it was listening. " +
                    "It is a dotnet tool: pin amazon.lambda.testtool in .config/dotnet-tools.json and run " +
                    "'dotnet tool restore', or run 'dotnet tool install -g amazon.lambda.testtool'. To run " +
                    $"against a tool started by hand instead, set {RuntimeApiVariable} to {plan.RuntimeApiEndpoint}.");
            }

            if (await IsListening(plan.EmulatorPort) && (!plan.ApiGateway || await IsListening(plan.GatewayPort))) {
                return;
            }

            if (DateTime.UtcNow > deadline) {
                throw new TimeoutException(
                    $"The AWS Lambda Test Tool did not start listening on {plan.EmulatorUrl} within {StartupTimeout.TotalSeconds:0} seconds.");
            }

            await Task.Delay(PollInterval);
        }
    }

    private static async Task<bool> IsListening(int port) {
        using var client = new TcpClient();

        try {
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(500));

            return true;
        }
        catch (SocketException) {
            return false;
        }
        catch (TimeoutException) {
            return false;
        }
    }

    /// <summary>
    /// Points the bootstrap at the tool by setting the variable the Lambda service would have set.
    /// </summary>
    private static void Point(LambdaEmulatorPlan plan) =>
        Environment.SetEnvironmentVariable(RuntimeApiVariable, plan.RuntimeApiEndpoint);

    // Plain lines rather than the structured logger: the reader is a person at a console, and the
    // Kestrel host prints the same "Listening on" line.
    private static void Announce(LambdaEmulatorPlan plan, bool started) {
        Console.Out.WriteLine(started
            ? $"Started the AWS Lambda Test Tool on {plan.EmulatorUrl}"
            : $"Using the AWS Lambda Test Tool already listening on {plan.EmulatorUrl}");

        if (plan.GatewayUrl != null) {
            Console.Out.WriteLine($"Listening on {plan.GatewayUrl}");
        }
    }
}
