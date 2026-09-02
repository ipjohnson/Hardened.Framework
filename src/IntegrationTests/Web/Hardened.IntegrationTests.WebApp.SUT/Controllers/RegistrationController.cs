using Hardened.IntegrationTests.WebApp.SUT.Models;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Attributes;
using ValidationModules;
// Both namespaces declare a ValidationException, which is CS0104 without this. Either reaches the
// same response - ExceptionToModelConverter maps one shape from both - so which is aliased is a
// choice, and Hardened's is the one this controller means.
using ValidationException = Hardened.Requests.Runtime.Validation.ValidationException;

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

    /// <summary>
    /// The same model on an operation that says what a validation refusal answers.
    /// </summary>
    /// <remarks>
    /// <c>[Throws&lt;RequestValidationError&gt;(422)]</c> puts the 422 in the published document,
    /// and the same declaration is what the runtime reads: one vocabulary carrying both, rather
    /// than an assertion on a verb attribute that the document would have to be told about
    /// separately. A specification-first operation declaring 422 has answered it since 0.18; this
    /// is how a hand-written one says the same thing.
    /// </remarks>
    [Post("/declared-422")]
    [Throws<RequestValidationError>(422)]
    public string RegisterDeclaring422(RegistrationModel model) => model.Name ?? "";

    /// <summary>
    /// A handler validating by hand on an operation that declares 422, so the thrown route to a
    /// refusal answers what the filter's route does.
    /// </summary>
    [Post("/declared-422/by-hand")]
    [Throws<RequestValidationError>(422)]
    public string ThrowValidation() =>
        throw new ValidationException(ValidationResult.FromErrors([
            new ValidationError("model.name", "required", "model.name is required.")
        ]));
}
