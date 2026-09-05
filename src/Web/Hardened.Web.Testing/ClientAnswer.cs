namespace Hardened.Web.Testing;

/// <summary>
/// What a call through a client was answered with, as the route that built the client read it.
/// </summary>
/// <remarks>
/// The three things <see cref="ClientAssertions.Returns{TExpected}"/> needs, and nothing about how
/// they were reached: a Kiota route reads them off a thrown model or a recorded response, a Refit
/// route off an envelope. <paramref name="Body"/> is the deserialised body - the value the client
/// returned, the model it threw, an error text read as the expectation's type - or null where the
/// status carried none.
/// </remarks>
/// <param name="Caveat">
/// A sentence added to a failure about this answer, for what the route knows and the failure would
/// otherwise not say - that a client threw its base exception because the document declared no
/// body for the status, so there was nothing to deserialise into.
/// </param>
public sealed record ClientAnswer(
    int Status,
    object? Body,
    IReadOnlyDictionary<string, string> Headers,
    string? Caveat = null);
