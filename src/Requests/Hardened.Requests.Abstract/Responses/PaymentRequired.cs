namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The request is well formed and the account cannot pay for it - 402.
/// </summary>
/// <remarks>
/// <para>
/// A quota or billing refusal, which is a different fact from a 403: the caller is who they say
/// they are and is allowed to do this, and the reason the answer is no is one they can settle
/// themselves. A 403 tells them to ask an administrator; this tells them to look at their plan.
/// </para>
/// <para>
/// RFC 9110 reserves 402 and defines nothing about it, so <see cref="Detail"/> is where the
/// service says which limit was reached. Nothing about the shape of a payment is asserted here,
/// because there is no interoperable shape to assert.
/// </para>
/// </remarks>
[HttpStatus(402)]
public sealed record PaymentRequired(string? Detail = null) : IHttpStatusResponse, IDeclaresStatus {

    public string Type => ProblemTypes.PaymentRequired;

    public string Title => "Payment Required";

    public static int StatusCode => 402;

    public int Status => StatusCode;
}
