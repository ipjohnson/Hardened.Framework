namespace Hardened.Amz.Shared.Lambda.Runtime;

/// <summary>
/// What the generated bootstraps resolve their services through, so that a missing module
/// attribute is reported as a missing module attribute.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrap a source generator emits resolves the transport's services in the application's
/// constructor, unconditionally. Applying <c>[HardenedModule]</c> and forgetting the transport
/// module is therefore not a compile error and not a routing failure - it is a container miss on
/// the first line of the constructor, and <c>GetRequiredService</c> reports it by naming the
/// interface:
/// </para>
/// <code>
/// No service for type 'Hardened.Web.Runtime.Handlers.IWebExecutionHandlerService' has been registered.
/// </code>
/// <para>
/// That names a type the consumer has never written down, in an assembly they may not know they
/// depend on, and says nothing about the attribute that would register it. It has cost this
/// repository three separate incidents: <c>LambdaWebTest</c> shipped without
/// <c>[LambdaWebModule]</c> and could not start, <c>SqsTest</c> and <c>DynamoDbStreamApp</c> the
/// same on the function side, and the README's own quick start carried the defect in print - the
/// documented example compiled and threw. Each was diagnosed from the source rather than from the
/// message.
/// </para>
/// <para>
/// The check is here rather than in the generator because the generator cannot see it. Whether a
/// module is present is a question about the built container, and modules compose - a consumer's
/// own <c>[DependencyModule]</c> may carry the transport module transitively, which no syntactic
/// check could follow without false positives. A false positive is a build failure on correct
/// code, which is worse than the message this replaces. Asking the container is exact.
/// </para>
/// </remarks>
public static class ApplicationServices {
    /// <summary>
    /// Resolves a service the generated bootstrap cannot run without, or throws naming the module
    /// attribute that registers it.
    /// </summary>
    /// <param name="serviceProvider">The application's root provider.</param>
    /// <param name="applicationName">
    /// The application class, so the message names the file to edit rather than leaving the reader
    /// to infer it from a stack trace.
    /// </param>
    /// <param name="moduleAttribute">
    /// The attribute that registers <typeparamref name="T"/>, without brackets.
    /// </param>
    /// <typeparam name="T">The service the bootstrap resolves.</typeparam>
    public static T Require<T>(IServiceProvider serviceProvider, string applicationName, string moduleAttribute)
        where T : class {
        if (serviceProvider.GetService(typeof(T)) is T service) {
            return service;
        }

        throw new InvalidOperationException(
            $"'{applicationName}' is missing [{moduleAttribute}]. The generated bootstrap resolves " +
            $"{typeof(T).FullName}, which [{moduleAttribute}] registers, so the application cannot be " +
            $"constructed without it. Add [{moduleAttribute}] to the class that carries " +
            "[HardenedModule]. See docs/application-types.md for the attribute each transport needs.");
    }
}
