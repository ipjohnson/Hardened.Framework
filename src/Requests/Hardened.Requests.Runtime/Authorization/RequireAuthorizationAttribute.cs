namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Makes a handler carrying no authorization attribute denied rather than public.
/// </summary>
/// <remarks>
/// <para>
/// Written on the application module class, or on the assembly when the module lives in another
/// project. <c>[BasePath]</c> already supports both targets for the same reason.
/// </para>
/// <example>
/// <code>
/// [HardenedModule]
/// [RequireAuthorization]
/// public partial class Application { }
/// </code>
/// </example>
/// <para>
/// <b>An attribute rather than a configuration flag, and the distinction is the whole point.</b>
/// Only syntax is visible to a source generator, and only a source generator can turn "you forgot
/// the attribute" into a build warning rather than a 403 somebody finds in staging. A startup call
/// can enforce at run time but cannot say anything at compile time - and the runtime-only version of
/// this feature is strictly worse than the fallback policy it would otherwise be copying.
/// </para>
/// <para>
/// It does both. The generator reports <c>HAUTH001</c> for every handler it can see that carries
/// neither a policy attribute nor <c>[AllowAnonymous]</c>, and it emits the registration that turns
/// on the runtime backstop - which is still required, because handlers can arrive from a referenced
/// assembly the generator never analysed.
/// </para>
/// <para>
/// The diagnostic is a warning by default so that adopting this does not break a large application
/// on day one. <c>TreatWarningsAsErrors</c> is on in CI, so an unannotated handler cannot merge
/// while still not blocking a refactor in progress. Raise it permanently with:
/// </para>
/// <code>
/// dotnet_diagnostic.HAUTH001.severity = error
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public class RequireAuthorizationAttribute : Attribute;
