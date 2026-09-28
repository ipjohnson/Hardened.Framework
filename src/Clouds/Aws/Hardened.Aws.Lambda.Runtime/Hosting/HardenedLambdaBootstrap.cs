using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Hardened.Aws.Lambda.Runtime.Development;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hardened.Aws.Lambda.Runtime.Hosting;

/// <summary>
/// Runs a Hardened application against the Lambda Runtime API.
/// </summary>
/// <remarks>
/// <para>
/// The whole of an application's entry point, once the container is built:
/// </para>
/// <code>
/// await HardenedLambdaBootstrap.Run(services.BuildServiceProvider());
/// </code>
/// <para>
/// <b>The container is built before the loop starts, on purpose.</b> Everything a request resolves
/// exists by the time the first invocation arrives, so a missing registration is a cold start that
/// fails immediately with the name of what was missing - rather than the first invocation of the
/// day failing while every subsequent one on a warm sandbox succeeds.
/// </para>
/// </remarks>
public static class HardenedLambdaBootstrap
{
    /// <summary>
    /// Serves invocations until the sandbox is shut down.
    /// </summary>
    /// <param name="serviceProvider">
    /// The application's root provider, which is what a generated <c>Application</c> exposes.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the loop. See the overload below for why a deployed function never uses it.
    /// </param>
    public static async Task Run(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default
    )
    {
        LambdaInvocationHandler? handler = null;

        // The startup services run as the bootstrap's initializer, not before the bootstrap is
        // built. Building it installs the runtime's log writer, which stamps each entry with its
        // request id and writes it in the function's log format. A startup service that logs before
        // then goes through Amazon.Lambda.Core's fallback instead: a text line on a JSON function,
        // with its message template unfilled. The initializer also runs in Lambda's init phase, so
        // a startup service that throws is reported to Lambda as an init error.
        using var bootstrap = new LambdaBootstrap(
            Handler(() => handler!),
            async () =>
            {
                // Through the guard, so the services run once per provider however the host was
                // reached - the same call KestrelServerRunner.StartAsync makes, and for the same
                // reason.
                //
                // Nothing here ran it, and a Lambda has no other start signal: the runtime hands over
                // an invocation and that is the whole lifecycle. So AuthenticationStartupService
                // installed no middleware, AuthorizationStartupService installed no filter provider
                // and CorsStartupService never ran - every request arrived anonymous, every route was
                // open, and nothing said so. Invisible from inside this repository because every test
                // host starts the provider itself, and only the deployed function was open.
                await ApplicationLogic.Start(serviceProvider, null);

                WarnWhenNotJson(serviceProvider);

                handler = serviceProvider.GetRequiredService<LambdaInvocationHandler>();

                return true;
            }
        );

        await bootstrap.RunAsync(cancellationToken);
    }

    /// <summary>
    /// Serves invocations against a handler built by hand, for a host that assembles its own.
    /// </summary>
    /// <param name="handler">
    /// The invocation handler to serve, for a host that resolved or built its own rather than
    /// taking it off an application's provider.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops the loop. A deployed function is never cancelled - the sandbox is frozen between
    /// invocations and eventually torn down - so this exists for a host that runs the loop as part
    /// of something larger, and for a test that has to get its process back.
    /// </param>
    public static async Task Run(
        LambdaInvocationHandler handler,
        CancellationToken cancellationToken = default
    )
    {
        // Constructed rather than built through LambdaBootstrapBuilder, whose Create overloads
        // resolve a lambda to Action<Stream, ILambdaContext, MemoryStream> before they reach
        // LambdaBootstrapHandler. Nothing in the builder is wanted here anyway: there is no
        // serializer to install, because an adapter binds its own payload.
        using var bootstrap = new LambdaBootstrap(Handler(() => handler));

        await bootstrap.RunAsync(cancellationToken);
    }

    /// <summary>
    /// The Runtime API's own contract, wrapped around one invocation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>disposeOutputStream</c> is left to the bootstrap, which reads the stream and then disposes
    /// it. The handler returns a <c>MemoryStream</c> positioned at zero for that reason.
    /// </para>
    /// <para>
    /// The handler is read at each invocation because <see cref="Run(IServiceProvider,
    /// CancellationToken)"/> resolves it in the initializer, after the bootstrap is built.
    /// </para>
    /// </remarks>
    private static LambdaBootstrapHandler Handler(Func<LambdaInvocationHandler> handler) =>
        async request => new InvocationResponse(
            await handler().Invoke(request.InputStream, request.LambdaContext)
        );

    /// <summary>
    /// Says at startup when the function logs in Lambda's text format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A warning rather than a switch, because the format belongs to the function. Lambda sets
    /// <c>AWS_LAMBDA_LOG_FORMAT</c> from the function's logging configuration, and the runtime
    /// frames every record it sends to Lambda by that variable. Setting it here would send JSON
    /// records to a function configured for text, which Lambda does not document.
    /// </para>
    /// <para>
    /// In the bootstrap rather than in a startup service, because every test host starts the
    /// startup services and none of them sets the variable.
    /// </para>
    /// </remarks>
    private static void WarnWhenNotJson(IServiceProvider serviceProvider)
    {
        var format = Environment.GetEnvironmentVariable(LambdaEmulator.LogFormatVariable);

        if (string.Equals(format, "JSON", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        serviceProvider
            .GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(HardenedLambdaBootstrap).FullName!)
            .LogWarning(
                "This function logs in Lambda's text format, because {Variable} is {Format}. Each "
                    + "entry reaches CloudWatch as a line of text, and the values in a message are not "
                    + "fields of their own. Set the function's log format to JSON: LogFormat in its "
                    + "LoggingConfig, loggingFormat in the CDK, or --logging-config LogFormat=JSON with "
                    + "the AWS CLI.",
                LambdaEmulator.LogFormatVariable,
                format ?? "not set"
            );
    }
}
