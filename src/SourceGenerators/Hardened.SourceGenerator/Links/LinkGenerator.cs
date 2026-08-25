using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Links;

/// <summary>
/// A links type per module, from the same handler models the route table comes from.
/// </summary>
/// <remarks>
/// <para>
/// Two shapes, because there are two questions. <c>{Entry}Routes.Products.GetProduct(42)</c> is a
/// static path builder answering "what is the route" - no context, no allocation, usable from
/// anywhere. <c>{Entry}Links</c> is instance-based and answers "what should a client call", which
/// is a different question on the framework's primary host: API Gateway strips the stage before the
/// application sees the path, so a root-relative link built from the route alone 404s. See
/// <c>ILinkContext</c>.
/// </para>
/// <para>
/// The names are derived from the controller and the method rather than declared, so a rename is a
/// compile error at every call site rather than a runtime miss. Rails' <c>product_path</c> and
/// Flask's <c>url_for</c> are runtime lookups: the same mistake fails when someone loads the page.
/// RazorBlade copies <c>@</c> expressions verbatim and emits <c>#line</c> directives with exact
/// spans, so a route change breaks the <c>.cshtml</c> at build time, reported at its own line and
/// column.
/// </para>
/// <para>
/// Emitted wherever there are routes rather than behind <c>[Enable&lt;T&gt;]</c>. It has no
/// third-party dependency - strings and an interface - and the common case is an API with no views
/// at all that still wants links for <c>Location</c> headers, which are exactly the strings that
/// rot.
/// </para>
/// </remarks>
public static class LinkGenerator {

    /// <summary>
    /// Both types are nested in the entry point rather than named after it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>App.Routes</c> and <c>App.Links</c> rather than <c>AppRoutes</c> and <c>AppLinks</c>. The
    /// suffix form only reads as "the routes of App" to someone who already knows the convention,
    /// and it puts two more types in the namespace beside the application for every application
    /// there is. Nesting also makes the pair visibly siblings, which the suffixes did not - they
    /// differ by a word that says nothing about one being static paths and the other resolved
    /// links.
    /// </para>
    /// <para>
    /// The entry point is already <c>partial</c>: <c>DependencyModules.SourceGenerator</c> emits
    /// <c>partial class App : IDependencyModule</c> into it, so a module that was not partial would
    /// fail there long before reaching here.
    /// </para>
    /// </remarks>
    private const string RoutesMemberName = "Routes";

    private const string LinksMemberName = "Links";

    /// <summary>The static path builder's type name, for a given entry point.</summary>
    public static string RoutesTypeName(EntryPointSelector.Model appModel) =>
        appModel.EntryPointType.Name + "." + RoutesMemberName;

    /// <summary>The context-aware links type's name, for a given entry point.</summary>
    public static string LinksTypeName(EntryPointSelector.Model appModel) =>
        appModel.EntryPointType.Name + "." + LinksMemberName;

    /// <summary>The links type as a reference, for generated code that has to name it.</summary>
    public static ITypeDefinition LinksType(EntryPointSelector.Model appModel) =>
        TypeDefinition.Get(appModel.EntryPointType.Namespace, LinksTypeName(appModel));

    public static void Generate(
        SourceProductionContext context,
        EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> handlers,
        string basePath) {
        // Emitted even when a module declares no routes, so anything generated beside it - a
        // template base with a Links property - can name the type without first knowing whether
        // there were any. An empty links type costs a few lines; a conditional one costs every
        // generator that references it a way to find out.
        var groups = Group(appModel, handlers, basePath);

        context.AddSource(
            appModel.EntryPointType.Name + ".Links",
            Write(appModel, groups));
    }

    /// <summary>One entry per link method, in a stable order.</summary>
    private readonly struct Link {
        public Link(string group, string name, string body, IReadOnlyList<Parameter> parameters) {
            Group = group;
            Name = name;
            Body = body;
            Parameters = parameters;
        }

        public string Group { get; }

        public string Name { get; }

        /// <summary>The route as a C# expression, already concatenated.</summary>
        public string Body { get; }

        public IReadOnlyList<Parameter> Parameters { get; }
    }

    private readonly struct Parameter {
        public Parameter(ITypeDefinition type, string name) {
            Type = type;
            Name = name;
        }

        public ITypeDefinition Type { get; }

        public string Name { get; }
    }

    private static IReadOnlyList<IGrouping<string, Link>> Group(
        EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> handlers,
        string basePath) {
        var links = new List<Link>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var handler in handlers.OrderBy(h => h.Name.Path, StringComparer.Ordinal)
                     .ThenBy(h => h.Name.Method, StringComparer.Ordinal)) {
            var link = Build(handler, basePath);

            // Two handlers whose links would have the same signature - the same group, name and
            // parameter types - cannot both be declared. C# forbids it, and emitting both would
            // produce output that does not compile, which is a worse failure than one route being
            // unreachable by name. Ordered by route so which one survives does not depend on the
            // order handlers happened to arrive in.
            if (seen.Add(Signature(link))) {
                links.Add(link);
            }
        }

        return links.GroupBy(link => link.Group).OrderBy(group => group.Key, StringComparer.Ordinal).ToList();
    }

    private static string Signature(Link link) =>
        link.Group + "." + link.Name + "(" + string.Join(",", link.Parameters.Select(p => p.Type.ToString())) + ")";

    /// <summary>
    /// One handler as a link: the route template with its tokens replaced by the parameters that
    /// bind to them.
    /// </summary>
    private static Link Build(RequestHandlerModel handler, string basePath) {
        var template = RoutePath.Combine(basePath, handler.Name.Path);
        var body = new StringBuilder();
        var parameters = new List<Parameter>();
        var literal = new StringBuilder();

        var index = 0;

        while (index < template.Length) {
            var open = template.IndexOf('{', index);

            if (open < 0) {
                literal.Append(template, index, template.Length - index);

                break;
            }

            var close = template.IndexOf('}', open);

            if (close < 0) {
                literal.Append(template, index, template.Length - index);

                break;
            }

            literal.Append(template, index, open - index);

            var token = template.Substring(open + 1, close - open - 1);
            var name = RouteTokens.Name(token);
            var parameter = Bound(handler, name);

            if (literal.Length > 0) {
                Append(body, Quote(literal.ToString()));
                literal.Clear();
            }

            Append(body, Value(parameter, RouteTokens.IsCatchAll(token)));
            parameters.Add(parameter);

            index = close + 1;
        }

        if (literal.Length > 0 || body.Length == 0) {
            Append(body, Quote(literal.ToString()));
        }

        var group = HandlerGroup.Identifier(handler);

        // A member may not share its enclosing type's name - CS0542 - and a HealthController with a
        // Health() handler is an ordinary thing to write. The verb is what distinguishes it, and
        // reads as a name rather than as a workaround.
        var method = string.Equals(handler.HandlerMethod, group, StringComparison.Ordinal)
            ? handler.HandlerMethod + Pascal(handler.Name.Method)
            : handler.HandlerMethod;

        return new Link(group, method, body.ToString(), parameters);
    }

    /// <summary>
    /// The handler parameter a token binds to, or a string parameter named after the token.
    /// </summary>
    /// <remarks>
    /// The fallback covers a route declaring a token no parameter binds - legal, and the route
    /// still needs a value in that position to be linkable at all.
    /// </remarks>
    private static Parameter Bound(RequestHandlerModel handler, string token) {
        foreach (var parameter in handler.RequestParameterInformationList) {
            if (parameter.BindingType == ParameterBindType.Path &&
                string.Equals(parameter.Name, token, StringComparison.Ordinal)) {
                return new Parameter(parameter.ParameterType, parameter.Name);
            }
        }

        return new Parameter(TypeDefinition.Get(typeof(string)), token);
    }

    /// <summary>
    /// A parameter as the text that goes in the URL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A string is escaped, because a value containing <c>/</c>, <c>?</c> or <c>#</c> would
    /// otherwise change which route the link points at - which is the failure a typed builder
    /// exists to prevent, arriving by another door.
    /// </para>
    /// <para>
    /// A catch-all is not escaped: it is a path fragment by definition, and encoding its separators
    /// would produce a link that no longer matches the route it was built from.
    /// </para>
    /// <para>
    /// Anything else goes through <c>Convert.ToString</c> with the invariant culture. Plain
    /// concatenation would format a number or a date in the ambient culture, so the same code would
    /// produce a different URL on a machine with a different locale.
    /// </para>
    /// </remarks>
    private static string Value(Parameter parameter, bool catchAll) {
        if (parameter.Type.Name == "String" || parameter.Type.Name == "string") {
            return catchAll
                ? parameter.Name
                : "global::System.Uri.EscapeDataString(" + parameter.Name + ")";
        }

        return "global::System.Convert.ToString(" + parameter.Name +
               ", global::System.Globalization.CultureInfo.InvariantCulture)";
    }

    private static void Append(StringBuilder body, string part) {
        if (body.Length > 0) {
            body.Append(" + ");
        }

        body.Append(part);
    }

    private static string Pascal(string value) =>
        value.Length == 0
            ? value
            : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Write(
        EntryPointSelector.Model appModel, IReadOnlyList<IGrouping<string, Link>> groups) {
        var file = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        // One more partial declaration of the application, carrying both types. The module
        // generator writes its own into the same class from a different file.
        var app = file.AddClass(appModel.EntryPointType.Name);

        app.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;

        WriteRoutes(app, appModel, groups);
        WriteLinks(app, appModel, groups);

        var outputContext = new OutputContext(new OutputContextOptions {
            TypeOutputMode = TypeOutputMode.Global
        });

        file.WriteOutput(outputContext);

        return outputContext.Output();
    }

    /// <summary>
    /// The static half: the route, with no idea where the application is deployed.
    /// </summary>
    private static void WriteRoutes(
        IConstructContainer file,
        EntryPointSelector.Model appModel,
        IReadOnlyList<IGrouping<string, Link>> groups) {
        var routes = file.AddClass(RoutesMemberName);

        routes.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        routes.Comment =
            $"The routes {appModel.EntryPointType.Name} declares, as paths. " +
            $"For a link a client can call, use {LinksTypeName(appModel)}.";

        foreach (var group in groups) {
            var groupClass = routes.AddClass(group.Key);

            groupClass.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;

            foreach (var link in group) {
                var method = groupClass.AddMethod(link.Name);

                method.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
                method.SetReturnType(typeof(string));

                foreach (var parameter in link.Parameters) {
                    method.AddParameter(parameter.Type, parameter.Name);
                }

                method.AddIndentedStatement(CodeOutputComponent.Get("return " + link.Body));
            }
        }
    }

    /// <summary>
    /// The instance half: the same routes, through whatever the transport did to the path.
    /// </summary>
    private static void WriteLinks(
        IConstructContainer file,
        EntryPointSelector.Model appModel,
        IReadOnlyList<IGrouping<string, Link>> groups) {
        var links = file.AddClass(LinksMemberName);

        links.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;
        links.Comment =
            $"Links to {appModel.EntryPointType.Name}'s routes, as a client would call them. " +
            "Resolve from the container, or read it off a template.";

        var contextField = links.AddField(KnownTypes.Requests.ILinkContext, "_context");

        contextField.Modifiers |= ComponentModifier.Private | ComponentModifier.Readonly;

        var constructor = links.AddConstructor();
        var contextParameter = constructor.AddParameter(KnownTypes.Requests.ILinkContext, "context");

        constructor.Assign(contextParameter).To(contextField.Instance);

        foreach (var group in groups) {
            var groupType = TypeDefinition.Get(
                appModel.EntryPointType.Namespace, LinksTypeName(appModel) + "." + group.Key + "Links");

            var property = links.AddProperty(groupType, group.Key);

            property.Modifiers |= ComponentModifier.Public;
            property.Set = null;
            property.Get.LambdaSyntax = true;
            property.Get.AddCode($"_{group.Key} ??= new {group.Key}Links(_context);");

            var backing = links.AddField(groupType.MakeNullable(), "_" + group.Key);

            backing.Modifiers |= ComponentModifier.Private;

            var groupClass = links.AddClass(group.Key + "Links");

            groupClass.Modifiers |= ComponentModifier.Public | ComponentModifier.Sealed;

            var groupField = groupClass.AddField(KnownTypes.Requests.ILinkContext, "_context");

            groupField.Modifiers |= ComponentModifier.Private | ComponentModifier.Readonly;

            var groupConstructor = groupClass.AddConstructor();
            var groupParameter =
                groupConstructor.AddParameter(KnownTypes.Requests.ILinkContext, "context");

            groupConstructor.Assign(groupParameter).To(groupField.Instance);

            foreach (var link in group) {
                WriteLinkMethod(appModel, groupClass, group.Key, link, "Resolve", link.Name);
                WriteLinkMethod(appModel, groupClass, group.Key, link, "Absolute", link.Name + "Absolute");
            }
        }

        WriteImportedLinks(links, appModel, groups);
    }

    /// <summary>
    /// A property per imported module that publishes links, so every route the application serves
    /// is reachable from the one type a view is handed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Links are generated per module. An application that keeps its routes in a library therefore
    /// had an empty <c>ApplicationLinks</c> and a <c>CatalogLibraryLinks</c> holding everything it
    /// serves - and a view's generated <c>Links</c> property is typed as the first, so
    /// <c>@Links.Book.ById(id)</c> did not compile. The build-time guarantee that a renamed route
    /// breaks the <c>.cshtml</c> was unavailable to exactly the applications that split into
    /// libraries.
    /// </para>
    /// <para>
    /// Named for the module rather than for anything shorter - <c>@Links.CatalogLibrary.Book.ById(id)</c>
    /// - because the module's name is the one thing already written at the import site, and any
    /// trimming rule would be a second name to learn and to get wrong.
    /// </para>
    /// <para>
    /// Constructed rather than resolved: a links type is a wrapper over <c>ILinkContext</c> and
    /// takes nothing else, so there is no reason to make this depend on the imported module having
    /// registered it.
    /// </para>
    /// </remarks>
    private static void WriteImportedLinks(
        ClassDefinition links,
        EntryPointSelector.Model appModel,
        IReadOnlyList<IGrouping<string, Link>> groups) {
        foreach (var imported in appModel.ImportedLinks) {
            // A controller group of the same name owns the name: it is declared in this assembly,
            // and the imported module's links stay reachable by resolving their own type. Emitting
            // both would be a duplicate member and a confusing error in generated code.
            if (groups.Any(group => group.Key == imported.PropertyName)) {
                continue;
            }

            var property = links.AddProperty(imported.LinksType, imported.PropertyName);

            property.Modifiers |= ComponentModifier.Public;
            property.Set = null;
            property.Get.LambdaSyntax = true;
            property.Get.AddCode(
                $"_{imported.PropertyName} ??= new {imported.LinksType.Namespace}.{imported.LinksType.Name}(_context);");

            var backing = links.AddField(
                imported.LinksType.MakeNullable(), "_" + imported.PropertyName);

            backing.Modifiers |= ComponentModifier.Private;
        }
    }

    /// <summary>
    /// One link method, delegating to the static route builder so the route is written once.
    /// </summary>
    private static void WriteLinkMethod(
        EntryPointSelector.Model appModel,
        ClassDefinition groupClass,
        string group,
        Link link,
        string resolver,
        string name) {
        var method = groupClass.AddMethod(name);

        method.Modifiers |= ComponentModifier.Public;
        method.SetReturnType(typeof(string));

        var arguments = new StringBuilder();

        foreach (var parameter in link.Parameters) {
            method.AddParameter(parameter.Type, parameter.Name);

            if (arguments.Length > 0) {
                arguments.Append(", ");
            }

            arguments.Append(parameter.Name);
        }

        var route = "global::" + appModel.EntryPointType.Namespace + "." + RoutesTypeName(appModel) +
                    "." + group + "." + link.Name + "(" + arguments + ")";

        method.AddIndentedStatement(
            CodeOutputComponent.Get("return _context." + resolver + "(" + route + ")"));
    }
}
