namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Binds a parameter from a field of an <c>application/x-www-form-urlencoded</c> or
/// <c>multipart/form-data</c> body, or from a file part of the second.
/// </summary>
/// <remarks>
/// <para>
/// <example>
/// <code>
/// [Post("/sign-in")]
/// public IResult SignIn([FromForm] string username, [FromForm] string password) => …
/// </code>
/// </example>
/// </para>
/// <para>
/// <b>Explicit, not inferred.</b> A parameter the route does not declare binds from the body, and
/// switching that to a form field whenever the content type happened to be a form would make a
/// handler's binding depend on what the caller sent rather than on what the handler declared.
/// </para>
/// <para>
/// <b>Reads the body.</b> A handler cannot bind form fields and a body model at once - there is one
/// body and the two readings are different. The generator reports that combination rather than
/// leaving one of them to come back empty.
/// </para>
/// <para>
/// <b>A model binds one field per member.</b> A type the string converter cannot read from one
/// value is constructed from the fields its members are named by, the way <c>System.Text.Json</c>
/// names them. <c>[FromQueryString]</c> does the same from the query string.
/// </para>
/// <para>
/// <b>A file binds to <c>IFormFile</c>.</b> A part carrying a <c>filename</c> is a file, and a
/// parameter or model member typed <c>IFormFile</c>, <c>IFormFile?</c> or a list of them binds from
/// the parts sent under its name.
/// </para>
/// </remarks>
public class FromFormAttribute : Attribute
{
    public FromFormAttribute(string? name = null)
    {
        Name = name;
    }

    public string? Name { get; }
}
