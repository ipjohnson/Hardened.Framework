using System.Reflection;
using Hardened.Web.Testing;
using Refit;

namespace Hardened.Refit.Testing;

/// <summary>
/// Builds a Refit client over the pipeline, and reads what a call through one answered. Named by
/// <see cref="RefitTestingAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// A Refit client is an interface whose methods carry a verb attribute - <c>[Get]</c>,
/// <c>[Post]</c> and the rest, every one an <see cref="HttpMethodAttribute"/> - which is a shape
/// rather than a name. So this recognises every interface Refitter writes and any written by hand,
/// and builds each with <see cref="RestService.For(Type, HttpClient)"/> over the harness's own
/// <see cref="HttpClient"/>: the credential is already on it, and the interface's relative paths
/// resolve against its base address.
/// </para>
/// <para>
/// Default <see cref="RefitSettings"/>, which is System.Text.Json. A client that needs its own -
/// another serializer, an option the application's bodies depend on - declares an
/// <see cref="ITestClientFactory{TClient}"/> for the interface, which wins over this route, and
/// passes the settings to <c>RestService.For</c> itself.
/// </para>
/// <para>
/// Reading an answer is <see cref="RefitAnswers"/>: the envelope a method declared
/// <c>Task&lt;IApiResponse&lt;T&gt;&gt;</c> returns, or the <see cref="ApiException"/> a method
/// declared <c>Task&lt;T&gt;</c> throws for a refusal. A success returned as the body alone has
/// dropped its status and is not read at all, which the failure says.
/// </para>
/// </remarks>
public sealed class RefitClientRoute : ITestClientRoute, ITestClientReader {

    public bool CanBuild(Type clientType) {
        ArgumentNullException.ThrowIfNull(clientType);

        return clientType.IsInterface && Operations(clientType).Any();
    }

    public object Build(TestClientContext context, Type clientType) {
        ArgumentNullException.ThrowIfNull(context);

        if (!CanBuild(clientType)) {
            throw new InvalidOperationException(
                $"{clientType.FullName} is not a Refit client: it is not an interface declaring a " +
                "method with a Refit verb attribute.");
        }

        return RestService.For(clientType, context.Http);
    }

    public Task<ClientAnswer?> Read(object? result, Exception? thrown, Type? bodyType) =>
        RefitAnswers.Read(result, thrown, bodyType);

    public string Unreadable =>
        "A Refit method declared Task<IApiResponse<T>> returns an envelope that carries the status " +
        "and the headers, and this call returned the body alone; Refitter's --use-api-response " +
        "declares the envelope.";

    /// <summary>Every method that names a verb, on the interface and on the interfaces it extends.</summary>
    private static IEnumerable<MethodInfo> Operations(Type clientType) =>
        clientType.GetInterfaces()
            .Prepend(clientType)
            .SelectMany(contract => contract.GetMethods())
            .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());
}
