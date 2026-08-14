namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Binds a handler parameter from a request cookie.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="FromHeaderAttribute"/> and <see cref="FromQueryStringAttribute"/>,
/// which the attribute paradigm lacked entirely - a cookie was reachable only by taking
/// <c>IExecutionRequest</c> and parsing the raw strings by hand.
/// </remarks>
public class FromCookieAttribute : Attribute {
    public FromCookieAttribute(string? name = null) {
        Name = name;
    }

    public string? Name { get; }
}
