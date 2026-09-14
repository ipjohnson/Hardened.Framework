using System.Collections.Immutable;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Lambda;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// Everything one entry point emits, from one place.
/// </summary>
/// <remarks>
/// <para>
/// The routing table, the document, the handler catalog, the handlers for registered lambdas and
/// the interceptors that reach them. They are emitted together because they share one thing that
/// cannot be written twice: the document. A registered route's schemas belong in its
/// <c>components</c>, and its operation has to be written against the same identifiers, so the
/// halves the run time splices between and the operations it splices in come from a single pass.
/// </para>
/// <para>
/// Here rather than in <c>RoutingTableGenerator</c>, which the described front end compiles in and
/// which therefore cannot name anything under <c>Web/Lambda</c>.
/// </para>
/// </remarks>
internal static class WebPipeline
{
    public static void Generate(
        SourceProductionContext context,
        (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models,
        WebGeneratorOptions options,
        ImmutableArray<RouteConstraintModel> constraints,
        ImmutableArray<LambdaRouteModel?> lambdas
    )
    {
        // The same filter the table applies: a handler that was not generated must not be named by
        // anything downstream of it.
        var routable = models.Right.Where(handler => !handler.CannotBeEmitted()).ToList();

        var registered = lambdas
            .Where(route => route != null)
            .Select(route => route!)
            .OrderBy(route => route.Handler.InvokeHandlerType.Name, StringComparer.Ordinal)
            .ToList();

        var declared = options.RegistrationTypes;

        // Every operation a route could be registered at a computed path with: a controller's
        // handler, which registers by type and method, and a lambda, which registers as itself.
        var describable =
            declared.Count == 0 && registered.Count == 0
                ? null
                : routable.Concat(registered.Select(route => route.Handler)).ToList();

        var split =
            describable == null
                ? (OpenApiDocumentGenerator.SplitDocument?)null
                : OpenApiDocumentGenerator.Split(
                    models.Left,
                    routable,
                    RoutingTableGenerator.GetBasePath(models.Left),
                    OpenApiVersionFacts.Parse(options.OpenApiVersion)
                        ?? OpenApiVersionFacts.Default,
                    null,
                    describable
                );

        var operations = split?.Operations ?? (IReadOnlyList<string>)Array.Empty<string>();

        RoutingTableGenerator.GenerateRoute(
            context,
            models,
            options,
            constraints,
            RouteHandlerCatalogEmitter.For(
                models.Left,
                routable,
                constraints,
                declared,
                split,
                operations
            ),
            split
        );

        LambdaRouteEmitter.Generate(
            context,
            models.Left,
            registered,
            operations.Count == 0 ? Array.Empty<string>() : operations.Skip(routable.Count).ToList()
        );
    }
}
