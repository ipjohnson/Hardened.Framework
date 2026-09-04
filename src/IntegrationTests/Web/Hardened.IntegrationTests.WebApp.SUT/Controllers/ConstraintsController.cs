using Hardened.Web.Runtime.Attributes;
using ValidationModules.Constraints;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Constraints written on a hand-written handler's own parameters: a query value, a header, a
/// path token and a value the caller must send.
/// </summary>
/// <remarks>
/// Nothing here mentions validation. The constraint is the whole declaration, as it is on a body
/// model, and the same declaration is what the published document repeats as the parameter's
/// facets. Until the web generator read a parameter's constraints, each of these was HRDV001 and a
/// hand-written check throwing <c>ValidationException</c>, and the document said only "an integer".
/// </remarks>
[BasePath("/constraints")]
public class ConstraintsController {

    /// <summary>A bound on a query value, pathed under the query key.</summary>
    [Get("/precision")]
    public int Precision([FromQueryString] [Range(Min = 2, Max = 8)] int precision) => precision;

    /// <summary>A length on a header, pathed under the header's own name.</summary>
    [Get("/region")]
    public string Region([FromHeader("X-Region")] [StringLength(2, 2)] string region) => region;

    /// <summary>
    /// A bound behind a route constraint. The route decides whether the URL exists, so
    /// <c>/page/abc</c> is a 404; the constraint decides whether the value is acceptable, so
    /// <c>/page/0</c> is a 400.
    /// </summary>
    [Get("/page/{count:int}")]
    public int Page([Range(Min = 1, Max = 100)] int count) => count;

    /// <summary>
    /// A value the caller must send, on a type that can be absent, with a shape it must take.
    /// </summary>
    [Get("/tagged")]
    public string Tagged([FromQueryString] [Required] [Pattern("^[a-z]+$")] string? tag) => tag!;
}
