using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Binding from an <c>application/x-www-form-urlencoded</c> body, through the real pipeline.
/// </summary>
/// <remarks>
/// The wire format is a query string in the body, so most of it is uninteresting and covered by
/// the query-string tests. What these assert is the parts where the two genuinely differ, and the
/// parts where the body being a stream matters.
/// </remarks>
public class FormBindingTests
{
    private static Action<TestWebRequest> AsForm =>
        request =>
            request.Headers[KnownHeaders.ContentType] = KnownContentType.FormUrlEncodedStringValues;

    [HardenedTest]
    public async Task FieldsBindToParameters(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            "username=ada&password=hunter2",
            "/form/sign-in",
            AsForm
        );

        response.Assert.Ok();
        Assert.Equal("ada:hunter2", response.Deserialize<string>());
    }

    /// <summary>
    /// <c>+</c> is a space, which is the one way a form differs from a query string.
    /// </summary>
    /// <remarks>
    /// <c>Uri.UnescapeDataString</c> decodes <c>%20</c> and leaves a plus alone, so a parser shared
    /// with the query string would bind <c>"Ada+Lovelace"</c> for a field every browser on earth
    /// sends as <c>Ada Lovelace</c>. Silently, on every form post.
    /// </remarks>
    [HardenedTest]
    public async Task APlusIsASpace(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            "username=Ada+Lovelace&password=x",
            "/form/sign-in",
            AsForm
        );

        response.Assert.Ok();
        Assert.Equal("Ada Lovelace:x", response.Deserialize<string>());
    }

    /// <summary>
    /// And an escaped plus survives as a plus.
    /// </summary>
    /// <remarks>
    /// The decode replaces <c>+</c> before unescaping. The other order would turn <c>%2B</c> into a
    /// space, which is the one case escaping it exists to prevent.
    /// </remarks>
    [HardenedTest]
    public async Task AnEscapedPlusStaysAPlus(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post("username=a%2Bb&password=x", "/form/sign-in", AsForm);

        response.Assert.Ok();
        Assert.Equal("a+b:x", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task PercentEncodingIsDecoded(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            "username=ada%40example.com&password=x",
            "/form/sign-in",
            AsForm
        );

        response.Assert.Ok();
        Assert.Equal("ada@example.com:x", response.Deserialize<string>());
    }

    /// <summary>A field is converted the same way a query value is.</summary>
    [HardenedTest]
    public async Task AFieldConvertsToTheParameterType(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post("count=21", "/form/quantity", AsForm);

        response.Assert.Ok();
        Assert.Equal(42, response.Deserialize<int>());
    }

    /// <summary>The wire name and the parameter name can differ.</summary>
    [HardenedTest]
    public async Task AFieldCanBeRenamed(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post("user_name=ada", "/form/renamed", AsForm);

        response.Assert.Ok();
        Assert.Equal("ada", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AnAbsentFieldTakesItsDefault(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post("present=here", "/form/optional", AsForm);

        response.Assert.Ok();
        Assert.Equal("here:fallback", response.Deserialize<string>());
    }

    /// <summary>
    /// A body that is not a form answers 415, with the types a form handler reads.
    /// </summary>
    /// <remarks>
    /// It used to bind an empty form, so the caller was told a field it may well have sent was
    /// missing.
    /// </remarks>
    [HardenedTest]
    public async Task AJsonBodyOnAFormHandlerIsA415(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(new { present = "ignored" }, "/form/optional");

        Assert.Equal(415, response.StatusCode);
        Assert.Equal(
            "application/x-www-form-urlencoded, multipart/form-data",
            response.Headers[KnownHeaders.Accept].ToString()
        );
    }

    /// <summary>The SUT caps a form body at 100,000 bytes, url-encoded or multipart.</summary>
    [HardenedTest]
    public async Task AUrlEncodedBodyPastTheCapIsTooLarge(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            "present=" + new string('x', 100_000),
            "/form/optional",
            AsForm
        );

        Assert.Equal(413, response.StatusCode);
    }

    [HardenedTest]
    public async Task AUrlEncodedBodyWithTooManyFieldsIsInvalid(ITestWebApp testWebApp)
    {
        var fields = string.Join("&", Enumerable.Range(0, 1025).Select(i => "f" + i + "=1"));

        var response = await testWebApp.Post(fields, "/form/optional", AsForm);

        response.Assert.BadRequest();

        var error = Assert.Single(response.Deserialize<RequestValidationError>()!.Errors!);

        Assert.Equal("body", error.Field);
        Assert.Equal("invalid", error.Code);
        Assert.Equal("The body has more than 1024 fields.", error.Message);
    }

    private const string SearchBody =
        "page=417&size=38&status=paid&category=garden&sort=created&q=alpha+bravo&minPrice=1200&maxPrice=34000";

    private const string SearchEcho = """
        {"page":417,"size":38,"status":"paid","category":"garden","sort":"created","q":"alpha bravo","minPrice":1200,"maxPrice":34000}
        """;

    /// <summary>
    /// RequestBench's <c>forms.urlencoded</c> body bound to one model, and echoed with the numbers
    /// as numbers.
    /// </summary>
    [HardenedTest]
    public async Task AFormBindsToAModel(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(SearchBody, "/form/search", AsForm);

        response.Assert.Ok();
        Assert.Equal(SearchEcho, await response.ReadTextAsync());
    }

    /// <summary>A missing member is refused by the field the client should have sent.</summary>
    [HardenedTest]
    public async Task AMissingMemberIsRefusedByItsFieldName(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            SearchBody.Replace("size=38&", ""),
            "/form/search",
            AsForm
        );

        response.Assert.BadRequest();

        var field = Assert.Single(response.Deserialize<RequestValidationError>()!.Errors!);

        Assert.Equal("size", field.Field);
        Assert.Equal("required", field.Code);
    }

    /// <summary>A member renamed for JSON is renamed on the form too.</summary>
    [HardenedTest]
    public async Task ARenamedMemberBindsFromItsJsonName(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            "display_name=Ada&age=36&theme=dark",
            "/form/profile",
            AsForm
        );

        response.Assert.Ok();
        Assert.Equal("Ada:36:dark:", response.Deserialize<string>());
    }

    /// <summary>An absent field leaves the member's initializer in place.</summary>
    [HardenedTest]
    public async Task AnAbsentMemberKeepsItsInitializer(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post("display_name=Ada&age=36", "/form/profile", AsForm);

        response.Assert.Ok();
        Assert.Equal("Ada:36:light:", response.Deserialize<string>());
    }

    /// <summary>A field sent more than once fills a collection member.</summary>
    [HardenedTest]
    public async Task ARepeatedFieldFillsACollectionMember(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            "display_name=Ada&age=36&interests=math&interests=engines",
            "/form/profile",
            AsForm
        );

        response.Assert.Ok();
        Assert.Equal("Ada:36:light:math,engines", response.Deserialize<string>());
    }

    /// <summary>
    /// The model's own constraints are enforced once it is bound, the way a body model's are.
    /// </summary>
    [HardenedTest]
    public async Task AMembersConstraintIsEnforced(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post("display_name=Ada&age=12", "/form/profile", AsForm);

        response.Assert.BadRequest();
        Assert.Contains(
            response.Deserialize<RequestValidationError>()!.Errors!,
            error => error.Field.EndsWith("age", StringComparison.Ordinal)
        );
    }

    /// <summary>The same model binds from the query string.</summary>
    [HardenedTest]
    public async Task AQueryStringBindsToAModel(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Get("/binding/query-model?" + SearchBody);

        response.Assert.Ok();
        Assert.Equal(SearchEcho, await response.ReadTextAsync());
    }
}

/// <summary>
/// The form refusals on a real socket, where the body is Kestrel's and cannot seek.
/// </summary>
[KestrelRuntime]
public class FormRefusalsOverSocketTests
{
    [HardenedTest]
    public async Task AUrlEncodedBodyPastTheCapIsTooLarge(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(
            "present=" + new string('x', 100_000),
            "/form/optional",
            request =>
                request.Headers[KnownHeaders.ContentType] =
                    KnownContentType.FormUrlEncodedStringValues
        );

        Assert.Equal(413, response.StatusCode);
    }

    [HardenedTest]
    public async Task AJsonBodyOnAFormHandlerIsA415(ITestWebApp testWebApp)
    {
        var response = await testWebApp.Post(new { present = "ignored" }, "/form/optional");

        Assert.Equal(415, response.StatusCode);
        Assert.Equal(
            "application/x-www-form-urlencoded, multipart/form-data",
            response.Headers[KnownHeaders.Accept].ToString()
        );
    }
}
