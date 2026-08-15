namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Matches this module's routes without regard to case, as Hardened used to for every application.
///
/// <para>
/// The default is case-sensitive, which is what RFC 3986 says a path is. It also halves the
/// matcher's work - the generated comparison was
/// <c>charSpan[i] == 'a' || charSpan[i] == 'A'</c> for every character of every literal on every
/// request - removes the duplicate-URL problem that comes of one resource answering at many
/// spellings, and matches the OpenAPI contract, which has no notion of a case-insensitive path.
/// </para>
///
/// <para>
/// A build-time switch rather than a runtime one because the matcher is compiled: the comparison
/// is emitted into the routing table, and there is nothing left at run time to decide. On the
/// entry point rather than the project, because the routing table is already per module.
/// </para>
///
/// <para>
/// Reach for this to keep an existing application working while its URLs are being tidied up, not
/// as a setting to leave on.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class CaseInsensitiveRoutesAttribute : Attribute { }
