using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// The <c>readOnly</c> / <c>writeOnly</c> half of the suite.
/// </summary>
/// <remarks>
/// <para>
/// The schema declares one type for both directions, and this is what working with it looks like:
/// the request arrives with <c>Id</c> unset whatever the client sent, and the server fills it in
/// with <c>with</c>. That is a C# initializer, entirely separate from the JSON resolver, which is
/// why the property can be assignable here and unreachable from deserialization.
/// </para>
/// <para>
/// <c>Password</c> is the mirror: it arrives populated and never leaves, because the resolver gives
/// it no getter.
/// </para>
/// </remarks>
[Handler]
public class AccountServiceImpl : IAccountService {

    /// <summary>The last password received, so a test can prove it arrived at all.</summary>
    public static string? LastPasswordSeen { get; private set; }

    /// <summary>
    /// The last id received. Null is the correct value however hard the client tried: a read-only
    /// property is not a constructor parameter and the resolver gives it no setter.
    /// </summary>
    public static string? LastIdSeen { get; private set; }

    public Task<Account> CreateAccount(Account body) {
        LastPasswordSeen = body.Password;
        LastIdSeen = body.Id;

        return Task.FromResult(body with { Id = "server-assigned" });
    }
}
