namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// The OpenAPI tag the operations on this controller are grouped under, when the class name is
/// not the right answer.
///
/// <para>
/// The default is the controller's name with a <c>Controller</c> suffix stripped, so
/// <c>ProductsController</c> groups under <c>Products</c>. That is almost always right, and it is
/// deliberately not a new grouping construct: the controller already <em>is</em> the group, and
/// the document simply did not say so. Tags are what a specification-first build turns back into
/// service interfaces, so without them a round-tripped application collapses into one
/// <c>IDefaultService</c> and loses its controller structure entirely.
/// </para>
///
/// <para>
/// Use this where the class name and the public name of the group genuinely differ - a
/// <c>V2ProductsController</c> that should still document as <c>Products</c>, or a controller
/// named after an internal concept.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class TagAttribute : Attribute {
    public TagAttribute(string name) {
        Name = name;
    }

    public string Name { get; }
}
