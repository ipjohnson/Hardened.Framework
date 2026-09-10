using ValidationModules.Constraints;
using DataAnnotations = System.ComponentModel.DataAnnotations;

namespace Hardened.IntegrationTests.WebApp.SUT.Models;

/// <summary>
/// A body model with constraints on it and nothing else - no attribute pointing at a validator, no
/// registration call, no <c>[Validate]</c> on the handler that takes it.
/// </summary>
/// <remarks>
/// Both vocabularies on one type, because they are supposed to be indistinguishable by the time
/// anything acts on them: <c>Name</c> is constrained with DataAnnotations and <c>Age</c> with
/// ValidationModules', and a caller cannot tell from the response which produced their error. The
/// two declare colliding type names, hence the alias - that collision is the only thing about
/// mixing them that a consumer has to deal with.
/// </remarks>
public class RegistrationModel {

    /// <summary>The name the registration is filed under.</summary>
    [DataAnnotations.Required]
    [DataAnnotations.StringLength(20, MinimumLength = 3)]
    public string? Name { get; set; }

    [Range(18, 120)]
    public int Age { get; set; }

    [ValidateNested]
    public AddressModel? Address { get; set; }
}

/// <summary>
/// A member declared present by its type and by nothing else: no <c>[Required]</c>, no
/// <c>required</c> modifier, no constraint of any kind.
/// </summary>
/// <remarks>
/// The trial's blocker, in the shape it was found in. The published document says
/// <c>required: ["memberId"]</c> - written from the nullable annotation - and the validator is built
/// from <c>[Required]</c> alone, so <c>{}</c> used to answer 201 with a null in a domain whose C#
/// type says it cannot be there. Deliberately unconstrained: a <c>[StringLength]</c> here would have
/// given the model a validator, which is how the case hid on models that happened to carry one.
/// </remarks>
public record MemberRequest(string MemberId);

/// <summary>A second level, so a failure has a path to report rather than a field.</summary>
/// <remarks>Sealed because VM0031 (new in ValidationModules rc1012) otherwise asks what should
/// happen when a more derived value reaches [ValidateNested] - nothing derives from this.</remarks>
public sealed class AddressModel {

    [Required]
    public string? City { get; set; }

    [StringLength(2, 2)]
    public string? Country { get; set; }
}
