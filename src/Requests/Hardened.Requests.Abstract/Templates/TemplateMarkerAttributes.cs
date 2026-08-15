namespace Hardened.Requests.Abstract.Templates;

/// <summary>
/// The base class a generated template base derives from, declared on a template engine's feature
/// marker.
/// </summary>
/// <remarks>
/// <para>
/// This is what keeps the generator from knowing what any particular marker means. It resolves the
/// marker named in <c>[Enable&lt;T&gt;]</c>, reads this attribute, and emits a base deriving from
/// whatever it names. A Fluid or Mustache package ships its own marker with its own
/// <c>[TemplateBase]</c> and needs no generator change - which is the difference between an
/// extension point and a switch statement with a plausible name.
/// </para>
/// <para>
/// The argument is an <em>unbound</em> generic - <c>typeof(HardenedHtmlTemplate&lt;&gt;)</c> - and
/// that is why the marker is a separate non-generic type pointing at the base rather than being the
/// base. <c>typeof(X&lt;&gt;)</c> is legal in an attribute argument; an unbound generic as a type
/// argument, <c>[Enable&lt;HardenedHtmlTemplate&lt;&gt;&gt;]</c>, is not.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class TemplateBaseAttribute : Attribute {
    public TemplateBaseAttribute(Type baseType) {
        BaseType = baseType;
    }

    public Type BaseType { get; }
}

/// <summary>
/// What templates built on this marker's base produce, declared on the marker.
/// </summary>
/// <remarks>
/// On the marker rather than per template, because it follows from the base class: a base deriving
/// from RazorBlade's <c>HtmlTemplate</c> escapes its output and produces HTML, and one deriving
/// from <c>PlainTextTemplate</c> does not. A template that produces something else - a CSV, an SVG,
/// an iCal feed - gets its own marker and its own base, which is also how it gets a base whose
/// escaping is right for it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class TemplateContentTypeAttribute : Attribute {
    public TemplateContentTypeAttribute(string contentType) {
        ContentType = contentType;
    }

    public string ContentType { get; }
}
