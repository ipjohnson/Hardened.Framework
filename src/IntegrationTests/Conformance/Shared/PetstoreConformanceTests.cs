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

    private static string Head(string value) =>
        value.Length <= 200 ? value : value.Substring(0, 200) + "…";
}
