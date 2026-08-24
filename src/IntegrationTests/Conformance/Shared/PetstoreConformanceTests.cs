using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.Conformance;

/// <summary>
/// The behaviour every front-end must exhibit, expressed once and executed against all of them.
/// </summary>
/// <remarks>
/// <para>
/// Hardened has three ways to declare an API — C# attributes, an OpenAPI description, and a Smithy
/// model — and they are supposed to be three spellings of one framework. That is only true if a
/// feature reaching one reaches all three, and until now nothing checked it. A feature could be
/// built for one front-end, pass its own tests, and be reported as done.
/// </para>
/// <para>
/// To enrol a front-end, link this file into that front-end's test project and derive from it:
/// </para>
/// <code>
/// &lt;Compile Include="..\Shared\*.cs" Link="Conformance\%(FileName)%(Extension)"/&gt;
///
/// public class OpenApiPetstoreConformance : PetstoreConformanceTests {
///     protected override string FrontEnd => "OpenAPI";
/// }
/// </code>
/// <para>
/// <b>Linked source rather than a shared assembly, and that is not a style preference.</b> The test
/// harness collects its setup attributes from <c>methodInfo.DeclaringType.Assembly</c> — see
/// <c>DependencyModules.Testing.Impl.AttributeUtility</c>. For a test method inherited from another
/// assembly that resolves to <em>this</em> assembly, which carries neither <c>[WebTesting]</c> nor
/// the <c>[HardenedTestEntryPoint]</c> naming the application, so every parameter fails to resolve
/// with "Instances of abstract classes cannot be created". Compiling the suite into each test
/// project puts the declaring type in the assembly that names its own entry point.
/// </para>
/// <para>
/// <b>What this suite asserts, and what it deliberately does not.</b> It asserts framework
/// behaviour — status codes, routing, verb handling, what is served where. It does not assert that
/// the three return identical payloads, because they legitimately do not: the OpenAPI petstore
/// models a pet as <c>(id, name, tag?)</c> and the Smithy one as <c>(id, name, kind, tag?)</c> with
/// an enum and a wrapped output. Forcing those to match would mean rewriting two working
/// applications to agree about something that is the application's business rather than the
/// framework's.
/// </para>
/// <para>
/// The contract each application must satisfy is small and stated here: three operations —
/// <c>GET /pets</c>, <c>POST /pets</c>, <c>GET /pets/{petId}</c> — with pet <c>1</c> present and
/// <see cref="AbsentPetId"/> absent. Everything below is expressed in terms of that and nothing else.
/// </para>
/// </remarks>
public abstract class PetstoreConformanceTests {
    /// <summary>
    /// Names the front-end under test, so a failure says which of the three broke rather than only
    /// which behaviour did.
    /// </summary>
    protected abstract string FrontEnd { get; }

    /// <summary>
    /// An id no pet has, for the fixture under test.
    /// </summary>
    /// <remarks>
    /// Overridable because the id is the fixture's business and the behaviour is not. The OpenAPI
    /// petstore fabricates a pet for any id but the literal "missing", and existing tests depend on
    /// <c>/pets/7</c> answering 200 - so pinning one magic value here would mean rewriting a working
    /// fixture to satisfy the suite rather than the other way round.
    /// </remarks>
    protected virtual string AbsentPetId => "999";

    /// <summary>
    /// Where this application serves its API description.
    /// </summary>
    /// <remarks>
    /// The path is overridable because each front-end asks for publishing in its own vocabulary and
    /// picks its own default: code-first enables a feature marker carrying
    /// <c>[OpenApiDocumentPath("/openapi.json")]</c>, while a described application sets
    /// <c>PublishUrl</c> metadata on its spec item. What is not negotiable is that a description
    /// gets served at all, which is what the test below asserts.
    /// </remarks>
    protected abstract string DocumentPath { get; }

    /// <summary>
    /// A route this application declares as requiring an authenticated caller.
    /// </summary>
    /// <remarks>
    /// The path differs because each front-end declares authorization in its own vocabulary:
    /// <c>[AuthorizeGrants("pets:read")]</c> code-first, a <c>security</c> requirement in OpenAPI,
    /// and an auth scheme on the service in Smithy. What they can express also differs — Smithy has
    /// no scopes and so can require authentication but never a particular grant — which is why the
    /// assertion below is that an anonymous caller is refused, and not that a named scope is
    /// demanded. That is the behaviour all three can carry.
    /// </remarks>
    protected abstract string SecuredPath { get; }

    /// <summary>
    /// A pet id whose lookup raises the operation's declared 429 rather than answering.
    /// </summary>
    protected virtual string ThrottledPetId => "throttled";

    /// <summary>
    /// The status of the error the operation declares, as it appears in the published description.
    /// </summary>
    /// <remarks>
    /// A plain status rather than a schema name, because the three descriptions are not one format:
    /// OpenAPI is served as YAML keyed by status, and Smithy as its JSON AST where the status is an
    /// <c>@httpError</c> trait on a named shape. What both spell the same way is the number.
    /// </remarks>
    protected virtual string DeclaredErrorStatus => "429";

    /// <summary>A pet id that violates the constraint the operation declares on it.</summary>
    protected virtual string MalformedPetId => "NOT_A_VALID_ID";

    /// <summary>
    /// What this front-end answers when a path token violates its declared constraint.
    /// </summary>
    /// <remarks>
    /// <b>This is a known divergence, not a settled design.</b> Given the same declared intent —
    /// <c>{petId:slug}</c> code-first, <c>pattern: '^[a-z0-9-]+$'</c> in OpenAPI,
    /// <c>@pattern("^[a-z0-9-]+$")</c> in Smithy — the described front-ends answer 400 and
    /// code-first answers 404. Both are defensible on their own terms: code-first compiles a
    /// constraint into the route table, so violating it means the route did not match; a described
    /// front-end treats the same constraint as a validation rule on a route that did match.
    /// <para>
    /// They cannot both be right for one declaration, and a client cannot tell which it will get.
    /// Pinning the value per front-end keeps the divergence from widening while the question is
    /// open; when it is answered, the override below is deleted rather than the test.
    /// </para>
    /// </remarks>
    protected virtual int MalformedTokenStatus => 404;

    private string Because(string what) => $"[{FrontEnd}] {what}";

    [HardenedTest]
    public async Task ListPets_ReturnsOk(ITestWebApp app) {
        var response = await app.Get("/pets");

        Assert.True(response.StatusCode == 200,
            Because($"GET /pets answered {response.StatusCode}, expected 200."));
    }

    [HardenedTest]
    public async Task GetPet_WithAKnownId_ReturnsOk(ITestWebApp app) {
        var response = await app.Get("/pets/1");

        Assert.True(response.StatusCode == 200,
            Because($"GET /pets/1 answered {response.StatusCode}, expected 200."));
    }

    [HardenedTest]
    public async Task GetPet_WithAnUnknownId_ReturnsNotFound(ITestWebApp app) {
        var response = await app.Get($"/pets/{AbsentPetId}");

        Assert.True(response.StatusCode == 404,
            Because($"GET /pets/{AbsentPetId} answered {response.StatusCode}, expected 404 for an absent pet."));
    }

    /// <summary>
    /// An empty token is not a match, so the request reaches no route at all.
    /// </summary>
    /// <remarks>
    /// This is the defect the two routing tables disagreed about for six months. The attribute-routed
    /// table refused the empty token from 2026-08-16; the described table bound <c>""</c> and
    /// answered 400 from the binder, telling a client it had addressed a real endpoint incorrectly
    /// about a URL addressing no endpoint at all. One implementation now serves all three, and this
    /// is what keeps that true.
    /// </remarks>
    [HardenedTest]
    public async Task GetPet_WithAnEmptyToken_ReturnsNotFound(ITestWebApp app) {
        var response = await app.Get("/pets/");

        Assert.True(response.StatusCode == 404,
            Because($"GET /pets/ answered {response.StatusCode}, expected 404. " +
                    "400 means the empty token was bound and reached the binder."));
    }

    [HardenedTest]
    public async Task CreatePet_ReturnsCreated(ITestWebApp app) {
        var response = await app.Post(new { name = "Rex" }, "/pets");

        Assert.True(response.StatusCode == 201,
            Because($"POST /pets answered {response.StatusCode}, expected 201."));
    }

    /// <summary>
    /// A path that exists under another verb is a 405, not a 404 — and it says which verbs it has.
    /// </summary>
    [HardenedTest]
    public async Task Pets_WithAnUnsupportedVerb_ReturnsMethodNotAllowed(ITestWebApp app) {
        var response = await app.Delete("/pets");

        Assert.True(response.StatusCode == 405,
            Because($"DELETE /pets answered {response.StatusCode}, expected 405 " +
                    "because /pets exists under GET and POST."));

        Assert.True(response.Headers.ContainsKey("Allow"),
            Because("A 405 carried no Allow header, so a client cannot learn what the path accepts."));
    }

    /// <summary>
    /// A description is served at the path this application declared.
    /// </summary>
    /// <remarks>
    /// This is the row that prompted the suite. Publishing was reported as working for OpenAPI and
    /// missing for Smithy, and neither claim had ever been checked: the implementation lives in the
    /// shared ExtractSpecTask base that both formats derive from, and the Smithy fixture simply
    /// never set PublishUrl. It does now.
    /// </remarks>
    [HardenedTest]
    public async Task Description_IsServedAtTheDeclaredPath(ITestWebApp app) {
        var response = await app.Get(DocumentPath);

        Assert.True(response.StatusCode == 200,
            Because($"GET {DocumentPath} answered {response.StatusCode}, expected 200 - " +
                    "this application declares that it publishes its description there."));

        var body = await response.ReadTextAsync();

        Assert.True(body.Length > 0,
            Because($"GET {DocumentPath} answered 200 with an empty body, which reads as success " +
                    "from every angle and describes nothing."));

        // Formats differ - OpenAPI serves YAML, Smithy serves its JSON AST - so this asserts the
        // one thing both must contain rather than trying to parse either. Without it the test
        // passes against any 200, which is how a published document can be empty and unnoticed.
        Assert.True(body.Contains("/pets", StringComparison.Ordinal),
            Because($"The description served at {DocumentPath} does not mention /pets, so it is " +
                    $"not describing this application. First 200 characters: {Head(body)}"));
    }

    /// <summary>
    /// A declared-secure route refuses a caller who presents nothing.
    /// </summary>
    [HardenedTest]
    public async Task SecuredRoute_RefusesAnAnonymousCaller(ITestWebApp app) {
        var response = await app.Get(SecuredPath);

        Assert.True(response.StatusCode is 401 or 403,
            Because($"GET {SecuredPath} answered {response.StatusCode}, expected 401 or 403 — " +
                    "this application declares that route as requiring an authenticated caller. " +
                    "A 200 means the declaration was read and then not enforced."));
    }

    /// <summary>
    /// A handler raising an error its operation declares answers with that status, not 500.
    /// </summary>
    /// <remarks>
    /// Three spellings again. A described front-end generates one exception per operation and
    /// status — <c>GetPetTooManyRequestsException</c>, carrying the declared body type — while
    /// code-first throws a built-in response type wrapped in <c>ResponseException</c>. Both derive
    /// from <c>StatusCodeException</c>, which is where the two vocabularies already meet.
    /// </remarks>
    [HardenedTest]
    public async Task DeclaredError_AnswersItsDeclaredStatus(ITestWebApp app) {
        var response = await app.Get($"/pets/{ThrottledPetId}");

        Assert.True(response.StatusCode == 429,
            Because($"GET /pets/{ThrottledPetId} answered {response.StatusCode}, expected 429. " +
                    "A 500 means the declared error was raised and not recognised as a response."));
    }

    /// <summary>
    /// A path token violating its declared constraint is refused rather than reaching the handler.
    /// </summary>
    /// <remarks>
    /// The status is <see cref="MalformedTokenStatus"/> because the three front-ends currently
    /// disagree about it. What they agree on, and what this pins, is that the request is refused —
    /// a 200 would mean the constraint was declared and then not applied.
    /// </remarks>
    [HardenedTest]
    public async Task MalformedPathToken_IsRefused(ITestWebApp app) {
        var response = await app.Get($"/pets/{MalformedPetId}");

        Assert.True(response.StatusCode == MalformedTokenStatus,
            Because($"GET /pets/{MalformedPetId} answered {response.StatusCode}, expected " +
                    $"{MalformedTokenStatus}. A 200 means the declared constraint was not applied."));
    }

    /// <summary>
    /// An error the operation declares is in the description it publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of <see cref="DeclaredError_AnswersItsDeclaredStatus"/>. That one asserts the
    /// handler answers the status; this one asserts a client reading the published contract can
    /// find out it might. A framework can do the first and not the second, and that is the worse
    /// failure of the two: the API behaves correctly and documents itself as unable to.
    /// </para>
    /// <para>
    /// The three declare it differently — <c>[Throws&lt;RateLimited&gt;]</c> code-first, a response
    /// key in OpenAPI, an <c>errors</c> list with <c>@httpError</c> in Smithy — which is the point
    /// of asserting it here rather than in each front-end's own tests.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task DeclaredError_AppearsInTheDescription(ITestWebApp app) {
        var response = await app.Get(DocumentPath);

        response.Assert.Ok();

        var description = await response.ReadTextAsync();

        Assert.True(description.Contains(DeclaredErrorStatus, StringComparison.Ordinal),
            Because($"The description at {DocumentPath} does not mention {DeclaredErrorStatus}, so " +
                    "a client reading it cannot learn the operation can answer that. " +
                    $"First 200 characters: {Head(description)}"));
    }

    private static string Head(string value) =>
        value.Length <= 200 ? value : value.Substring(0, 200) + "…";
}
