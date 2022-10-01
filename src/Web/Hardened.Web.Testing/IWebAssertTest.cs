namespace Hardened.Web.Testing;

/// <summary>
/// interface for web asserts
/// </summary>
public interface IWebAssertThat
{
    /// <summary>
    /// Checks status code is a 2xx code
    /// </summary>
    void Ok();

    /// <summary>
    /// Assert status code is 404
    /// </summary>
    void NotFound();

    /// <summary>
    /// Assert status code is 400
    /// </summary>
    void BadRequest();

    /// <summary>
    /// Assert status code is 401
    /// </summary>
    void Unauthorized();

    /// <summary>
    /// Assert status code is 403
    /// </summary>
    void Forbidden();
}