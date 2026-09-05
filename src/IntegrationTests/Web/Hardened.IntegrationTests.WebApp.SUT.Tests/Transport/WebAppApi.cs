using Refit;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Transport;

/// <summary>
/// A Refit interface over the Web integration application, written by hand in the shape Refitter
/// writes from the exported document: <see cref="IApiResponse{T}"/> on every operation, which is
/// what carries a status and its headers back beside the body.
/// </summary>
/// <remarks>
/// <para>
/// The models are the application's own, which a hand-written interface can borrow and a
/// generated one would declare for itself. <see cref="GetItem"/> is deliberately the other shape,
/// returning the body alone, because that is the shape <c>Returns</c> has to refuse for a success.
/// Built for a test parameter by the route <c>[assembly: RefitTesting]</c> names in Bootstrap.cs.
/// </para>
/// <para>
/// A <c>string</c> body is the response text as it arrived, not a deserialised value: Refit reads
/// it raw, so an operation answering the JSON string <c>"pets"</c> is read with its quotes.
/// </para>
/// </remarks>
public interface IWebAppApi {

    [Get("/verbs/item/{id}")]
    Task<string> GetItem(string id);

    [Post("/verbs/located")]
    Task<IApiResponse<MathAddModel>> CreateLocated([Body] MathAddModel model);

    [Delete("/verbs/emptied")]
    Task<IApiResponse> Empty();

    [Post("/registration/declared-422")]
    Task<IApiResponse<string>> RegisterDeclaring422([Body] RegistrationModel model);

    [Get("/authorization/pets")]
    Task<IApiResponse<string>> Pets();

    [Post("/int/add")]
    Task<IApiResponse<int>> Add([Body] MathAddModel model);
}
