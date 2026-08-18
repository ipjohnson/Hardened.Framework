using Hardened.Requests.Abstract.Headers;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Binding from an <c>application/x-www-form-urlencoded</c> body, through the real pipeline.
/// </summary>
/// <remarks>
/// The wire format is a query string in the body, so most of it is uninteresting and covered by
/// the query-string tests. What these assert is the parts where the two genuinely differ, and the
/// parts where the body being a stream matters.
/// </remarks>
public class FormBindingTests {

    private static Action<TestWebRequest> AsForm =>
        request => request.Headers[KnownHeaders.ContentType] =
            KnownContentType.FormUrlEncodedStringValues;

    [HardenedTest]
    public async Task FieldsBindToParameters(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "username=ada&password=hunter2", "/form/sign-in", AsForm);

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
    public async Task APlusIsASpace(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "username=Ada+Lovelace&password=x", "/form/sign-in", AsForm);

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
    public async Task AnEscapedPlusStaysAPlus(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "username=a%2Bb&password=x", "/form/sign-in", AsForm);

        response.Assert.Ok();
        Assert.Equal("a+b:x", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task PercentEncodingIsDecoded(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "username=ada%40example.com&password=x", "/form/sign-in", AsForm);

        response.Assert.Ok();
        Assert.Equal("ada@example.com:x", response.Deserialize<string>());
    }

    /// <summary>A field is converted the same way a query value is.</summary>
    [HardenedTest]
    public async Task AFieldConvertsToTheParameterType(ITestWebApp testWebApp) {
        var response = await testWebApp.Post("count=21", "/form/quantity", AsForm);

        response.Assert.Ok();
        Assert.Equal(42, response.Deserialize<int>());
    }

    /// <summary>The wire name and the parameter name can differ.</summary>
    [HardenedTest]
    public async Task AFieldCanBeRenamed(ITestWebApp testWebApp) {
        var response = await testWebApp.Post("user_name=ada", "/form/renamed", AsForm);

        response.Assert.Ok();
        Assert.Equal("ada", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task AnAbsentFieldTakesItsDefault(ITestWebApp testWebApp) {
        var response = await testWebApp.Post("present=here", "/form/optional", AsForm);

        response.Assert.Ok();
        Assert.Equal("here:fallback", response.Deserialize<string>());
    }

    /// <summary>
    /// A request that sends no form at all binds an empty one rather than failing.
    /// </summary>
    /// <remarks>
    /// The reader answers for the content type, so a JSON body posted to a form handler is not a
    /// parse error - it is a form with no fields, and the fields come back missing the way an
    /// absent query parameter does. Never null, which is the point of returning
    /// <c>EmptyFormCollection</c> rather than a null collection.
    /// </remarks>
    [HardenedTest]
    public async Task AJsonBodyOnAFormHandlerBindsAnEmptyForm(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { present = "ignored" }, "/form/optional");

        response.Assert.BadRequest();
    }
}
