namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Binds a parameter from a field of an <c>application/x-www-form-urlencoded</c> body.
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
/// Fields only. <c>multipart/form-data</c>, which is what a form with a file input posts, is a
/// different wire format and is not read by this.
/// </para>
/// </remarks>
public class FromFormAttribute : Attribute {
    public FromFormAttribute(string? name = null) {
        Name = name;
    }

    public string? Name { get; }
}
