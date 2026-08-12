namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Routes a PUT request to the attributed handler.
///
/// <para>
/// The four status properties this used to declare were removed on 2026-08-11 — see
/// <see cref="GetAttribute"/> and docs/TESTING-PLAN.md §2.3.
/// </para>
/// </summary>
public class PutAttribute : Attribute {
    public PutAttribute(string path = "") {
        Path = path;
    }

    public string Path { get; }
}
