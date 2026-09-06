using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Conditional;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A header a handler writes itself, declared so the document carries it, and a class-level
/// <c>[ConditionalGet]</c> over a read and a write.
/// </summary>
/// <remarks>
/// <para>
/// Both halves of what a declaration can say about a document. <see cref="Create"/> writes
/// <c>Location</c> on the context, which is the only way to send one from a handler returning a
/// plain value - and the document described the response as bare, so a generated client had
/// nothing to read the new resource's address from. <c>[AnswersHeader]</c> is the handler saying
/// what it sends.
/// </para>
/// <para>
/// And the reach. <c>[ConditionalGet]</c> is on the class, so it installs on <see cref="Read"/>
/// and stands down on <see cref="Create"/>. The document has to do the same: a 304 on the POST
/// would be a status the operation cannot answer, and an <c>If-None-Match</c> parameter on it
/// would be a header nothing reads.
/// </para>
/// </remarks>
[BasePath("/declared-header")]
[ConditionalGet]
public class DeclaredHeaderController {

    /// <summary>Where <see cref="Create"/> says it put the note.</summary>
    public const string CreatedAt = "/declared-header/notes/1";

    [Get("/notes")]
    public string[] Read() => ["first"];

    [Post("/notes", SuccessStatus = 201)]
    [AnswersHeader(201, KnownHeaders.Location, Description = "Where the note was created.")]
    public string Create(IExecutionContext context) {
        context.Response.Headers[KnownHeaders.Location] = CreatedAt;

        return "created";
    }
}
