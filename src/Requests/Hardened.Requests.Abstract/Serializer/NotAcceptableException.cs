using Hardened.Requests.Abstract.Errors;

namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// A client asked for media types this operation does not produce.
/// </summary>
/// <remarks>
/// <para>
/// Carries a body naming what the operation <em>can</em> produce, which is the useful thing to say
/// and costs nothing: the client already knows what it asked for, so listing the alternatives is not
/// telling it anything about the service it could not have read in the document.
/// </para>
/// <para>
/// A <see cref="StatusCodeException"/> rather than a status set on the response, so it travels the
/// path every other declared status does and lands with a body instead of the empty 406
/// <c>NotAcceptable</c> used to write.
/// </para>
/// </remarks>
public class NotAcceptableException : StatusCodeException {
    public NotAcceptableException(IReadOnlyList<string> produced)
        : base(406,
            new ErrorModel {
                Type = "NotAcceptable",
                Message = Describe(produced),
                Details = string.Join(", ", produced)
            },
            // The same sentence as the body, because this reaches a log as well as a client and an
            // operator reading "The request produced status 406." learns nothing from it.
            Describe(produced)) { }

    private static string Describe(IReadOnlyList<string> produced) =>
        "This operation produces " + string.Join(", ", produced) + ".";
}
