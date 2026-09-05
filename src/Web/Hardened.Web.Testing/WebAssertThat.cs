namespace Hardened.Web.Testing;

public class WebAssertThat : IWebAssertThat {
    private readonly TestWebResponse _response;

    public WebAssertThat(TestWebResponse response) {
        _response = response;
    }

    /// <summary>
    /// Assert status is 200 - 299
    /// </summary>
    public void Ok() {
        var status = _response.StatusCode;

        if (status is < 200 or > 299) {
            throw new WebAssertionException($"Expected a 2xx status, the response was {status}.");
        }
    }

    /// <summary>
    /// Assert status is 404
    /// </summary>
    public void NotFound() => Expect(404);

    /// <summary>
    /// Assert status code is 400
    /// </summary>
    public void BadRequest() => Expect(400);

    /// <summary>
    /// Assert status is 401
    /// </summary>
    public void Unauthorized() => Expect(401);

    /// <summary>
    /// Assert status is 403
    /// </summary>
    public void Forbidden() => Expect(403);

    private void Expect(int status) {
        if (_response.StatusCode != status) {
            throw new WebAssertionException($"Expected status {status}, the response was {_response.StatusCode}.");
        }
    }
}
