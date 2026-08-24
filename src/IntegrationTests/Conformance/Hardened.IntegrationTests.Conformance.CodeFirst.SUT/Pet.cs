namespace Hardened.IntegrationTests.Conformance.CodeFirst.SUT;

/// <summary>
/// Deliberately not the same shape as the OpenAPI or Smithy petstore's pet.
/// </summary>
/// <remarks>
/// The OpenAPI fixture models a pet as (id, name, tag?) and the Smithy one as (id, name, kind, tag?)
/// with an enum and a wrapped output. The conformance suite asserts framework behaviour - status
/// codes, routing, verb handling - and not payload equality, because the payload is the
/// application's business. Keeping this shape distinct is what stops the suite quietly growing
/// assertions that only hold because three fixtures were made to agree by hand.
/// </remarks>
public record Pet(string Id, string Name, string? Tag = null);

public record CreatePetRequest(string Name, string? Tag = null);
