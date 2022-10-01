using Xunit;

namespace Hardened.Web.Testing;

public class WebAssertThat : IWebAssertThat
{
    private readonly TestWebResponse _response;

    public WebAssertThat(TestWebResponse response)
    {
        _response = response;
    }

    /// <summary>
    /// Assert status is 200 - 299
    /// </summary>
    public void Ok()
    {
        Assert.InRange(_response.StatusCode, 200, 299);
    }

    /// <summary>
    /// Assert status is 404
    /// </summary>
    public void NotFound()
    {
        Assert.Equal(404, _response.StatusCode);
    }

    /// <summary>
    /// Assert status code is 400
    /// </summary>
    public void BadRequest()
    {
        Assert.Equal(400, _response.StatusCode);
    }

    /// <summary>
    /// Assert status is 401
    /// </summary>
    public void Unauthorized()
    {
        Assert.Equal(401, _response.StatusCode);
    }

    /// <summary>
    /// Assert status is 403
    /// </summary>
    public void Forbidden()
    {
        Assert.Equal(403, _response.StatusCode);
    }
}