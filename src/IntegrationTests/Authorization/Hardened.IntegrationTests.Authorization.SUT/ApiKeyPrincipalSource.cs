using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.IntegrationTests.Authorization.SUT;

/// <summary>A scheme, named as a type, the way <c>[Authorize&lt;TScheme&gt;]</c> names one.</summary>
public class ApiKeyScheme : IAuthenticationScheme;

/// <summary>
/// A source written against the typed interface and registered with the plain attribute.
/// </summary>
/// <remarks>
/// <para>
/// CS-01 and SU-04, as a fixture. A registration attribute registers a class as the interface it
/// declares, so this lands in the container under <c>IPrincipalSource&lt;ApiKeyScheme&gt;</c> and
/// under nothing else - and while the startup service resolved the non-generic interface alone,
/// an application whose only source looked like this installed no middleware, answered 401 to
/// every protected route, and said nothing about why. Two arms lost a debugging session to it.
/// </para>
/// <para>
/// Declines every request that does not carry its own header, so the fixture's other source
/// answers for every test written before this one.
/// </para>
/// </remarks>
[SingletonService]
public class ApiKeyPrincipalSource : IPrincipalSource<ApiKeyScheme> {
    public const string KeyHeader = "X-Api-Key";

    /// <summary>The one key this fixture knows, and the grant it carries.</summary>
    public const string KnownKey = "open-sesame";

    public const string KeyGrant = "pets:read";

    public ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context) {
        if (!context.Request.Headers.TryGetValue(KeyHeader, out var header) ||
            header.ToString() != KnownKey) {
            return new ValueTask<ICallerPrincipal?>((ICallerPrincipal?)null);
        }

        return new ValueTask<ICallerPrincipal?>(
            new CallerPrincipal("api-key", [KeyGrant], subject: "api-key-caller"));
    }
}
