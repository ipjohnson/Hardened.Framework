using Hardened.Requests.Abstract.Responses;

namespace Hardened.Requests.Abstract.Errors;

/// <summary>
/// How a refused or failed request is answered when the contract declared no body for it: a
/// problem details document as RFC 9457 defines it.
/// </summary>
/// <remarks>
/// <para>
/// <c>type</c> names the status, as <c>urn:hardened:problem:</c> followed by its reason phrase in
/// lower case with hyphens, and <c>title</c> is the reason phrase. They are the same values the
/// problem records in <c>Hardened.Web.Runtime.Responses</c> send, so a 404 the framework raises and
/// a <c>NotFound</c> a handler returns read the same to a client. <c>detail</c> is what went wrong
/// this time.
/// </para>
/// <para>
/// It implements <see cref="IProblemDetails"/>, so a JSON serializer sends it as
/// <c>application/problem+json</c>.
/// </para>
/// </remarks>
public class ErrorModel : IProblemDetails
{
    /// <summary>The prefix of every problem type the framework sends.</summary>
    public const string TypePrefix = "urn:hardened:problem:";

    /// <summary>What went wrong in this occurrence, or null.</summary>
    public string? Detail { get; set; }

    /// <summary>The URI identifying the kind of problem.</summary>
    public string Type { get; set; } = "about:blank";

    /// <summary>The reason phrase of <see cref="Status"/>.</summary>
    public string Title { get; set; } = "";

    /// <summary>The HTTP status the problem is answered with.</summary>
    public int Status { get; set; }

    /// <summary>
    /// The document for <paramref name="status"/>, with <paramref name="detail"/> as its detail.
    /// </summary>
    public static ErrorModel For(int status, string? detail = null) =>
        new()
        {
            Detail = detail,
            Type = TypeFor(status),
            Title = HttpStatusText.ReasonPhrase(status) ?? status.ToString(),
            Status = status,
        };

    /// <summary>
    /// The problem type for <paramref name="status"/>, or <c>about:blank</c> for a status with no
    /// reason phrase.
    /// </summary>
    /// <remarks>
    /// 429 is <c>rate-limited</c> rather than the reason phrase, because that is the name of the
    /// record a handler returns for it.
    /// </remarks>
    public static string TypeFor(int status)
    {
        if (status == 429)
        {
            return TypePrefix + "rate-limited";
        }

        var phrase = HttpStatusText.ReasonPhrase(status);

        if (phrase == null || status < 400)
        {
            return "about:blank";
        }

        return TypePrefix + phrase.ToLowerInvariant().Replace(' ', '-');
    }
}
