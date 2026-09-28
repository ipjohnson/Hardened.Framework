using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Jwt;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Runtime.DependencyInjection;

namespace Hardened.IntegrationTests.Jwt.SUT;

[HttpAuthenticationScheme("bearer", BearerFormat = "JWT")]
public sealed class BearerAuth : IAuthenticationScheme;

/// <summary>
/// An application that authenticates bearer tokens as JWTs, and nothing else.
/// </summary>
[HardenedModule]
[HardenedWebModule]
[JwtBearerAuthentication<BearerAuth>]
public partial class JwtApp;
