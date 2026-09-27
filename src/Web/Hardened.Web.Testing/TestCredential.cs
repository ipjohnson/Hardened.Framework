using System.Reflection;
using DependencyModules.Testing.Attributes.Interfaces;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Testing;

/// <summary>
/// Who a test's requests are sent as: the grants, the subject and the claims
/// <see cref="TestGrantsPrincipalSource"/> reads off the test headers.
/// </summary>
/// <remarks>
/// <para>
/// Credentials travel as headers rather than as an object a client is handed, because that is the
/// one thing every client has in common. A Kiota client, an NSwag client, a Refit interface and a
/// hand-written class all send whatever their <see cref="HttpClient"/> carries, so a credential
/// applied to the client's default headers authenticates any of them with no code of their own -
/// and the same headers on <c>app.Get</c> make the harness and the client one caller.
/// </para>
/// <para>
/// Usually resolved from <see cref="GrantsAttribute"/>, <see cref="SubjectAttribute"/>,
/// <see cref="ClaimAttribute"/> and <see cref="AnonymousAttribute"/> rather than constructed.
/// Constructed for a caller decided inside the test, and handed to
/// <see cref="ITestWebApp.CreateClient{TClient}"/>.
/// </para>
/// </remarks>
/// <param name="Grants">
/// The grants the caller holds, or null for a caller who presented nothing. An empty list is a
/// caller who is authenticated and holds nothing, which the source spells as <c>-</c>.
/// </param>
/// <param name="Subject">Which caller, or null for the source's default subject.</param>
public sealed record TestCredential(IReadOnlyList<string>? Grants, string? Subject = null)
{
    /// <summary>No headers at all: the request stays anonymous.</summary>
    public static readonly TestCredential Anonymous = new(Grants: null);

    /// <summary>
    /// The claims the caller carries, as <c>X-Test-Claims</c>, or null for none: what a policy reads
    /// through <c>ICallerPrincipal.TryGetClaim</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Claims { get; init; }

    /// <summary>Whether this credential sends nothing.</summary>
    public bool IsAnonymous => Grants == null && Subject == null && ClaimsHeaderValue == null;

    /// <summary>
    /// Sets the test headers on <paramref name="headers"/> when the caller set none of them, so a
    /// test that wrote its own credential into the request keeps it.
    /// </summary>
    public void ApplyTo(IDictionary<string, StringValues> headers)
    {
        if (
            IsAnonymous
            || headers.ContainsKey(TestGrantsPrincipalSource.GrantsHeader)
            || headers.ContainsKey(TestGrantsPrincipalSource.SubjectHeader)
            || headers.ContainsKey(TestGrantsPrincipalSource.ClaimsHeader)
        )
        {
            return;
        }

        headers[TestGrantsPrincipalSource.GrantsHeader] = GrantsHeaderValue;

        if (Subject != null)
        {
            headers[TestGrantsPrincipalSource.SubjectHeader] = Subject;
        }

        if (ClaimsHeaderValue is { } claims)
        {
            headers[TestGrantsPrincipalSource.ClaimsHeader] = claims;
        }
    }

    /// <summary>
    /// Sets the test headers on a request that carries none of them, in a socket host's chain - the
    /// same rule the pipeline host applies to the execution request it builds.
    /// </summary>
    internal void ApplyTo(HttpRequestMessage request)
    {
        if (
            IsAnonymous
            || request.Headers.Contains(TestGrantsPrincipalSource.GrantsHeader)
            || request.Headers.Contains(TestGrantsPrincipalSource.SubjectHeader)
            || request.Headers.Contains(TestGrantsPrincipalSource.ClaimsHeader)
        )
        {
            return;
        }

        ApplyTo(request.Headers);
    }

    /// <summary>Sets the test headers as the client's defaults, so every request it sends carries them.</summary>
    public void ApplyTo(HttpClient client)
    {
        if (IsAnonymous)
        {
            return;
        }

        ApplyTo(client.DefaultRequestHeaders);
    }

    private void ApplyTo(System.Net.Http.Headers.HttpHeaders headers)
    {
        headers.TryAddWithoutValidation(TestGrantsPrincipalSource.GrantsHeader, GrantsHeaderValue);

        if (Subject != null)
        {
            headers.TryAddWithoutValidation(TestGrantsPrincipalSource.SubjectHeader, Subject);
        }

        if (ClaimsHeaderValue is { } claims)
        {
            headers.TryAddWithoutValidation(TestGrantsPrincipalSource.ClaimsHeader, claims);
        }
    }

    /// <summary>
    /// The grants header. A subject with no grants is still an authenticated caller, which the
    /// source spells with its anonymous-grants value rather than by omitting the header.
    /// </summary>
    private string GrantsHeaderValue =>
        Grants == null || Grants.Count == 0
            ? TestGrantsPrincipalSource.AnonymousGrantsValue
            : string.Join(" ", Grants);

    private string? ClaimsHeaderValue =>
        Claims == null || Claims.Count == 0 ? null : TestGrantsPrincipalSource.FormatClaims(Claims);

    /// <summary>
    /// The credential the attributes in scope resolve to: the assembly's, then the class's, then
    /// the method's, then the parameter's, each narrower one applied over the wider.
    /// </summary>
    /// <remarks>
    /// <see cref="ITestMethodContext.Attributes"/> already arrives widest first, so applying in
    /// order makes the last match win, which is the rule <c>[EnvironmentName]</c> follows. A
    /// parameter's attributes come from the parameter itself and are applied last.
    /// <see cref="AnonymousAttribute"/> at any level resets what the wider levels said.
    /// </remarks>
    public static TestCredential Resolve(
        ITestMethodContext testMethod,
        ParameterInfo? parameter = null
    )
    {
        var attributes = testMethod.Attributes.AsEnumerable();

        if (parameter != null)
        {
            attributes = attributes.Concat(parameter.GetCustomAttributes());
        }

        return Resolve(attributes);
    }

    /// <summary>Applies every credential attribute in <paramref name="widestFirst"/>, in order.</summary>
    public static TestCredential Resolve(IEnumerable<Attribute> widestFirst)
    {
        var credential = Anonymous;

        foreach (var attribute in widestFirst)
        {
            if (attribute is TestCredentialAttribute credentialAttribute)
            {
                credential = credentialAttribute.Apply(credential);
            }
        }

        return credential;
    }
}

/// <summary>
/// What the credential attributes share: a step over the credential resolved so far, and the
/// parameter hook that builds a client for a parameter carrying one.
/// </summary>
/// <remarks>
/// A parameter attribute implements <see cref="ITestParameterValueProvider"/> so the runner asks it
/// for the value, which is what lets two parameters of one client type carry two credentials:
/// the attributed parameter is built here with its own, the bare one resolves the instance
/// <see cref="WebTestingAttribute"/> registered with the method's. On a method, a class or the
/// assembly the runner never calls the hook; only the <see cref="Apply"/> step is read.
/// </remarks>
public abstract class TestCredentialAttribute : Attribute, ITestParameterValueProvider
{
    internal abstract TestCredential Apply(TestCredential current);

    void ITestParameterValueProvider.SetupServiceCollection(
        ITestMethodContext testMethod,
        Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection,
        ParameterInfo parameter
    ) { }

    Task<object?> ITestParameterValueProvider.GetParameterValueAsync(
        ITestMethodContext testMethod,
        IServiceProvider serviceProvider,
        ParameterInfo parameter
    ) => Task.FromResult(TestClientBuilder.ForParameter(testMethod, serviceProvider, parameter));
}

/// <summary>
/// The grants the caller holds, as <c>X-Test-Grants</c>.
/// </summary>
/// <remarks>
/// On a parameter, a method, a class or the assembly; the narrowest wins. Where one is nested in
/// another only the grants change - a <see cref="SubjectAttribute"/> on the class still names the
/// caller a method-level <c>[Grants]</c> sends as.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Parameter
        | AttributeTargets.Method
        | AttributeTargets.Class
        | AttributeTargets.Assembly
)]
public sealed class GrantsAttribute : TestCredentialAttribute
{
    public GrantsAttribute(params string[] grants)
    {
        Grants = grants;
    }

    public IReadOnlyList<string> Grants { get; }

    internal override TestCredential Apply(TestCredential current) =>
        current with
        {
            Grants = Grants,
        };
}

/// <summary>
/// Which caller, as <c>X-Test-Subject</c>, for a test where one caller's data reaching another
/// is the thing being asserted.
/// </summary>
/// <remarks>
/// A subject with no grants in scope is still sent, as a caller who is authenticated and holds
/// nothing; the source spells that <c>-</c>.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Parameter
        | AttributeTargets.Method
        | AttributeTargets.Class
        | AttributeTargets.Assembly
)]
public sealed class SubjectAttribute : TestCredentialAttribute
{
    public SubjectAttribute(string subject)
    {
        Subject = subject;
    }

    public string Subject { get; }

    internal override TestCredential Apply(TestCredential current) =>
        current with
        {
            Subject = Subject,
        };
}

/// <summary>
/// A claim the caller carries, as <c>X-Test-Claims</c>, for a policy that reads one: a tenant, an
/// organisation.
/// </summary>
/// <remarks>
/// One per claim, as many as the caller needs. On a parameter, a method, a class or the assembly; a
/// narrower one replaces a wider one's value for the same name and keeps the others. The grants and
/// the subject are kept, and <see cref="AnonymousAttribute"/> clears the claims with the rest.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Parameter
        | AttributeTargets.Method
        | AttributeTargets.Class
        | AttributeTargets.Assembly,
    AllowMultiple = true
)]
public sealed class ClaimAttribute : TestCredentialAttribute
{
    public ClaimAttribute(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }

    internal override TestCredential Apply(TestCredential current)
    {
        var claims = new Dictionary<string, string>(StringComparer.Ordinal);

        if (current.Claims != null)
        {
            foreach (var claim in current.Claims)
            {
                claims[claim.Key] = claim.Value;
            }
        }

        claims[Name] = Value;

        return current with
        {
            Claims = claims,
        };
    }
}

/// <summary>
/// No credential, cancelling whatever a wider level declared: a class of tests that all hold a
/// grant, and the one that asserts the refusal.
/// </summary>
[AttributeUsage(
    AttributeTargets.Parameter
        | AttributeTargets.Method
        | AttributeTargets.Class
        | AttributeTargets.Assembly
)]
public sealed class AnonymousAttribute : TestCredentialAttribute
{
    internal override TestCredential Apply(TestCredential current) => TestCredential.Anonymous;
}
