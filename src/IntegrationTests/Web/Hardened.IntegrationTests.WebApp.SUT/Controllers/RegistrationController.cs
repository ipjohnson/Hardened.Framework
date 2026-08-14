using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A hand-written controller whose body model carries constraints, and which says nothing about
/// validation itself.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of this controller is what is <em>not</em> written in it. There is no
/// <c>[Validate&lt;T&gt;]</c>, no filter attribute, no registration - the constraints are on
/// <see cref="RegistrationModel"/> and nowhere else. Until the generator attached a filter here,
/// this was the gap: a validator was emitted for the model and registered, and the handler ran
/// without ever calling it, so a request violating its own declared constraints came back 200.
/// </para>
/// <para>
/// <see cref="Unconstrained"/> is the control. A handler whose types constrain nothing must not
/// gain a filter, because attaching one everywhere would put the cost of validation on requests
/// that have nothing to validate - and would hide the case above rather than fix it.
/// </para>
/// </remarks>
[BasePath("/registration")]
public class RegistrationController {

    [Post("/")]
    public string Register(RegistrationModel model) => model.Name ?? "";

    /// <summary>The same model reached with a path token beside it, so both sources coexist.</summary>
    [Post("/for/{tenant}")]
    public string RegisterForTenant(string tenant, RegistrationModel model) => $"{tenant}:{model.Name}";

    [Post("/anonymous")]
    public string Unconstrained(MathAddModel model) =>
        string.Join(",", model.Values ?? new List<int>());
}
