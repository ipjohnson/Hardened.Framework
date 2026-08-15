using System.Text;

namespace Hardened.SourceGenerator.Models.Request;

/// <summary>
/// Which group a handler belongs to: its controller, named the way the outside world sees it.
/// </summary>
/// <remarks>
/// One derivation, used by the two things that need to name a group - the OpenAPI document's tags
/// and the generated links type. They must agree, or a route name would change meaning when the
/// document round-trips, which is the whole reason tags were added before links.
/// </remarks>
public static class HandlerGroup {
    private const string ControllerSuffix = "Controller";

    private const string ServiceSuffix = "Service";

    /// <summary>
    /// The group's name: what <c>[Tag]</c> declared, or the controller's own name with the
    /// framework's own affixes removed.
    /// </summary>
    /// <remarks>
    /// Two shapes, because there are two directions. An attribute-routed application declares
    /// <c>ProductsController</c>, and a specification-first one implements <c>IProductService</c> -
    /// a name a document's <c>Products</c> tag was turned into by <c>NamingHelper.ToInterfaceName</c>.
    /// Undoing that here is what makes a link name the same in both, which is the same round-trip
    /// property tags were added for.
    /// </remarks>
    public static string Name(RequestHandlerModel handler) {
        if (!string.IsNullOrEmpty(handler.Tag)) {
            return handler.Tag!;
        }

        var name = handler.ControllerType.Name;

        if (name.Length > ControllerSuffix.Length &&
            name.EndsWith(ControllerSuffix, StringComparison.Ordinal)) {
            return name.Substring(0, name.Length - ControllerSuffix.Length);
        }

        // I{Tag}Service, the shape a tag becomes on the specification-first side. The uppercase
        // test is what keeps a controller genuinely called "InvoiceService" from losing its I.
        if (name.Length > ServiceSuffix.Length + 1 &&
            name[0] == 'I' &&
            char.IsUpper(name[1]) &&
            name.EndsWith(ServiceSuffix, StringComparison.Ordinal)) {
            return name.Substring(1, name.Length - ServiceSuffix.Length - 1);
        }

        return name;
    }

    /// <summary>
    /// The same name as a C# identifier, for generated code that has to declare a type after it.
    /// </summary>
    /// <remarks>
    /// A tag is free text - <c>[Tag("Pet Store")]</c> and <c>[Tag("v2.products")]</c> are both
    /// legal and neither is an identifier. Separators become word boundaries rather than being
    /// dropped, so "pet-store" and "pet store" both come out <c>PetStore</c> rather than
    /// <c>Petstore</c>.
    /// </remarks>
    public static string Identifier(RequestHandlerModel handler) => Identifier(Name(handler));

    public static string Identifier(string name) {
        var builder = new StringBuilder(name.Length);
        var capitalise = true;

        foreach (var character in name) {
            if (char.IsLetterOrDigit(character)) {
                builder.Append(capitalise ? char.ToUpperInvariant(character) : character);
                capitalise = false;
            }
            else {
                capitalise = true;
            }
        }

        if (builder.Length == 0) {
            return "Default";
        }

        // An identifier cannot start with a digit, and a tag legitimately can - "2024Reports".
        return char.IsDigit(builder[0]) ? "_" + builder : builder.ToString();
    }
}
