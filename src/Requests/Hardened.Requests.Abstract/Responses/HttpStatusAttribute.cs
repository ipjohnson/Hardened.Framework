namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The status a response type carries by default.
/// </summary>
/// <remarks>
/// <para>
/// Layer 1 of the four-layer status resolution: the type's own default, which a response set or an
/// endpoint can override. It is what makes a type→status answer possible at all, and that answer is
/// what the whole response-set model rests on - a single envelope covering every status has no
/// type→status mapping and cannot be described in a document.
/// </para>
/// <para>
/// Read at compile time by the generator rather than at run time by reflection, which is why this
/// carries the status and <see cref="IHttpStatusResponse"/> also does. They are not duplication of
/// the same consumer: an attribute is what a user's own type can declare without implementing a
/// framework interface, and an interface is what the pipeline can read without reflecting over
/// attributes in a trimmed application.
/// </para>
/// <para>
/// <c>Inherited = false</c> deliberately. A status inherited by a derived type is the assignability
/// hazard the diagnostics part of the plan exists to reject - two types in one response set where
/// one is assignable to the other have no unambiguous match order. The built-in types are sealed
/// for the same reason.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class HttpStatusAttribute : Attribute {

    public HttpStatusAttribute(int statusCode) {
        StatusCode = statusCode;
    }

    /// <summary>The status a response of this type is written with.</summary>
    public int StatusCode { get; }
}
