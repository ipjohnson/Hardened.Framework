namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Routes a DELETE request to the attributed handler.
///
/// <para>
/// The four status properties this used to declare were removed on 2026-08-11 — see
/// <see cref="GetAttribute"/> and docs/TESTING-PLAN.md §2.3.
/// </para>
/// </summary>
public class DeleteAttribute : Attribute {
    public DeleteAttribute(string path = "") {
        Path = path;
    }

    public string Path { get; }
}
