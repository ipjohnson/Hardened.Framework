using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web.Lambda;

/// <summary>
/// The handler classes for the lambdas a compilation registers, and the interceptors that reach
/// them.
/// </summary>
/// <remarks>
/// <para>
/// One file for the whole compilation rather than one per call site, because the interceptors have
/// to share a static class and the <c>[InterceptsLocation]</c> attribute has to be declared once.
/// </para>
/// <para>
/// <b>Why an interceptor rather than a lookup.</b> The alternative is caller info - a table keyed
/// by file and line that the registry probes - which needs no opt-in but turns a compile-time
/// guarantee into a runtime one that can miss, on a key that moves when the file does. Rewriting
/// the call means the generated code is the only code that can run, and the declared method is left
/// to throw.
/// </para>
/// </remarks>
internal static class LambdaRouteEmitter
{
    public const string InterceptorClass = "RegisteredRouteInterceptors";

    private const string Registry = "global::Hardened.Web.Runtime.Routing.IRouteRegistry";

    private const string Handler = "global::Hardened.Web.Runtime.Routing.RegisteredRouteHandler";

    /// <summary>
    /// Emits the file, or nothing where the compilation registers no lambda.
    /// </summary>
    public static void Generate(
        SourceProductionContext context,
        ImmutableArray<LambdaRouteModel?> found
    )
    {
        // Ordered so the emitted file does not reshuffle between builds, which would dirty the
        // incremental cache over nothing.
        var routes = found
            .Where(route => route != null)
            .Select(route => route!)
            .OrderBy(route => route.Handler.InvokeHandlerType.Name, StringComparer.Ordinal)
            .ToList();

        if (routes.Count == 0)
        {
            return;
        }

        context.AddSource(
            "RegisteredRouteHandlers.Lambdas",
            GeneratedSource.Header(
                Write(
                    routes[0].Handler.InvokeHandlerType.Namespace,
                    routes,
                    context.CancellationToken
                )
            )
        );
    }

    public static string Write(
        string handlerNamespace,
        IReadOnlyList<LambdaRouteModel> routes,
        CancellationToken cancellationToken
    )
    {
        var file = new CSharpFileDefinition(handlerNamespace);

        // AddSingleton and GetRequiredService are extension methods, and an extension method is
        // reachable only through a using of its namespace - global:: cannot name one. The routing
        // table's file says the same thing for the same reason.
        file.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        foreach (var route in routes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            InvokeClassGenerator.GenerateInvokeClass(route.Handler, file, cancellationToken);
        }

        var interceptors = file.AddClass(InterceptorClass);

        interceptors.Modifiers |= ComponentModifier.Static | ComponentModifier.Internal;
        interceptors.Comment =
            "Rewrites each registration call to one that carries the handler the build emitted for "
            + "its lambda. The declared method throws; see IRouteRegistry.";

        for (var index = 0; index < routes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            interceptors.AddComponent(new CodeOutputComponent(Interceptor(routes[index], index)));
        }

        var outputContext = new OutputContext(
            new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global }
        );

        file.WriteOutput(outputContext);

        // After the file rather than before it. A using directive has to precede every other member
        // of a compilation unit, and CSharpAuthor writes the usings at the top of what it renders -
        // so a namespace declaration in front of that output is CS1529.
        return outputContext.Output() + Attribute();
    }

    /// <summary>
    /// One interceptor, as written text.
    /// </summary>
    /// <remarks>
    /// An extension method, which is what Roslyn requires of an interceptor for an instance call -
    /// the receiver becomes the first argument. CSharpAuthor has no <c>this</c> parameter, so this
    /// one member is written rather than built; the file around it is not.
    /// </remarks>
    private static string Interceptor(LambdaRouteModel route, int index)
    {
        var cast = "(" + Written(route.DelegateType) + ")handler";

        return route.InterceptsAttribute
            + "\npublic static "
            + Registry
            + " "
            + route.Method
            + "_"
            + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "(this "
            + Registry
            + (route.NamesVerb ? " routes, string method, string path" : " routes, string path")
            + ", global::System.Delegate handler) =>\n    routes.Map(path, new "
            + Handler
            + "(\n        \""
            + route.Method
            + "\",\n        "
            + Tokens(route)
            + ",\n        (serviceProvider, routePath) => new global::"
            + route.Handler.InvokeHandlerType.Namespace
            + "."
            + route.Handler.InvokeHandlerType.Name
            + "(serviceProvider, "
            + cast
            + ", routePath)));";
    }

    /// <summary>
    /// A type as the cast has to spell it, arguments included.
    /// </summary>
    /// <remarks>
    /// Written rather than rendered through CSharpAuthor, because this appears inside a member the
    /// file's own writer never sees.
    /// </remarks>
    private static string Written(ITypeDefinition type)
    {
        // A keyword type carries no namespace, and global::int is not a type.
        var name =
            type.Namespace.Length == 0 ? type.Name : "global::" + type.Namespace + "." + type.Name;

        if (type is GenericTypeDefinition generic && generic.TypeArguments.Count > 0)
        {
            name += "<" + string.Join(", ", generic.TypeArguments.Select(Written)) + ">";
        }

        if (type.IsArray)
        {
            name += "[]";
        }

        // The delegate declares int? where the bound parameter is int, so the cast has to say so or
        // it is a conversion the compiler refuses.
        return type.IsNullable ? name + "?" : name;
    }

    private static string Tokens(LambdaRouteModel route) =>
        route.BoundTokens.Count == 0
            ? "global::System.Array.Empty<string>()"
            : "new string[] { "
                + string.Join(", ", route.BoundTokens.Select(name => "\"" + name + "\""))
                + " }";

    /// <summary>
    /// The attribute the compiler reads, declared here because net8.0 does not have it.
    /// </summary>
    /// <remarks>
    /// <c>file</c>-scoped, so an application whose target framework does declare it, or which runs
    /// a second generator that emits its own, has one per file rather than a collision.
    /// </remarks>
    private static string Attribute() =>
        "\nnamespace System.Runtime.CompilerServices\n"
        + "{\n"
        + "    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]\n"
        + "    file sealed class InterceptsLocationAttribute : global::System.Attribute\n"
        + "    {\n"
        + "        public InterceptsLocationAttribute(int version, string data)\n"
        + "        {\n"
        + "            _ = version;\n"
        + "            _ = data;\n"
        + "        }\n"
        + "    }\n"
        + "}\n";
}
