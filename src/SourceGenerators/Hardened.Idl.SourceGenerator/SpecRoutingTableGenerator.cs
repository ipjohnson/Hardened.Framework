using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using Hardened.Idl.Models;
using Hardened.SourceGenerator.Links;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;

namespace Hardened.Idl.SourceGenerator;

/// <summary>
/// The description-driven routing table.
/// </summary>
/// <remarks>
/// <para>
/// There is no route tree here. This settles the configuration the shared generator needs, emits the
/// service registrations that only a described application has, and hands both to
/// <see cref="RoutingTableGenerator"/>.
/// </para>
/// <para>
/// It used to hold its own copy of the walk — 625 lines, taken at 63% similarity when the OpenAPI
/// generator was added and drifted apart for six months afterwards. What the copy cost is on the
/// record: an empty path token bound to <c>""</c> and answered 400 from the binder for a URL
/// matching no route, <c>IsDecimal</c> took a thousands separator, custom constraints were accepted
/// and never applied, and a catch-all both refused any path holding a separator and bound its token
/// under the name <c>*path</c>. Every one of those was fixed in the attribute-routed copy and in
/// neither case ported here.
/// </para>
/// <para>
/// The registrations below stay because they are not route-tree code: they depend on
/// <see cref="HandlerInfo"/> and <see cref="SpecRegistration"/>, both internal to this assembly.
/// Sharing them would mean making those public to move logic the walk never touched.
/// </para>
/// </remarks>
internal static class SpecRoutingTableGenerator {
    public static void GenerateRoute(
        SourceProductionContext context,
        (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models,
        ImmutableArray<HandlerInfo?> handlerInfos,
        ImmutableArray<SpecRegistration> specRegistrations,
        IReadOnlyList<RouteConstraintModel> constraints,
        bool excludeFromCoverage = false) {
        var outputString = GenerateCSharpRouteFile(
            models.Left, models.Right, handlerInfos, specRegistrations,
            context.CancellationToken, excludeFromCoverage, constraints);

        context.AddSource(models.Left.EntryPointType.Name + ".SpecRouting", outputString);

        // The same links an attribute-routed application gets, from the same models. A document
        // generates the routes, so a link built from one is checked against the document rather
        // than against a hand-written route.
        LinkGenerator.Generate(context, models.Left, models.Right, "");
    }

    public static string GenerateCSharpRouteFile(
        EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> handlers,
        ImmutableArray<HandlerInfo?> handlerInfos,
        ImmutableArray<SpecRegistration> specRegistrations,
        CancellationToken cancellationToken,
        bool excludeFromCoverage = false,
        IReadOnlyList<RouteConstraintModel>? constraints = null) {
        // Ordered so the emitted table does not reshuffle between builds.
        var ordered = specRegistrations
            .OrderBy(registration => registration.ResolverName, StringComparer.Ordinal)
            .ThenBy(registration => registration.PublishUrl, StringComparer.Ordinal)
            .ToList();

        var options = new RoutingTableOptions {
            ClassName = "SpecRoutingTable",
            HintSuffix = ".SpecRouting",
            DependencyFieldName = "_openApiRoutingTableDependencies",
            DependencyMethodName = "SpecRoutingTableDI",
            TypeOutputMode = TypeOutputMode.Global,
            ExcludeFromCodeCoverage = excludeFromCoverage,

            // Generated from a document, so there is no document to re-derive.
            EmitOpenApiDocument = false,

            // A described route already carries whatever prefix the description gave it.
            UseEntryPointBasePath = false,

            // Interface-to-implementation pairs are registered below instead.
            RegisterControllerTypes = false,

            // A description can now contribute a route constraint, so the declarations the
            // compilation carries - including the ones the build task emitted for this spec - have
            // to reach the table that compiles them in.
            Constraints = constraints,

            AdditionalRegistrations =
                Registrations(appModel, handlers, handlerInfos, ordered, cancellationToken)
        };

        return RoutingTableGenerator.GenerateCSharpRouteFile(
            appModel, handlers, cancellationToken, options);
    }

    /// <summary>
    /// What a described application registers and an attribute-routed one does not.
    /// </summary>
    private static IReadOnlyList<IOutputComponent> Registrations(
        EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> handlers,
        ImmutableArray<HandlerInfo?> handlerInfos,
        IReadOnlyList<SpecRegistration> ordered,
        CancellationToken cancellationToken) {
        var statements = new List<IOutputComponent>();

        // The OpenAPI-generated JSON type info resolvers, for AOT serialization.
        //
        // One per spec file, by the name the build task emitted and recorded in the model. This used
        // to derive a single "{RootNamespace}.Models.OpenApiJsonTypeInfoResolver" from the first
        // handler's namespace, which meant two spec files in one project emitted two classes of that
        // one name and the project did not compile - finding 3.1.
        foreach (var registration in ordered) {
            if (registration.ResolverName.Length == 0) {
                continue;
            }

            statements.Add(new CodeOutputComponent(
                $"serviceCollection.AddSingleton(typeof(global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver), global::{registration.ResolverName}.Instance)"));

            // The enum converters the same resolver holds, as the parameter binder consumes them.
            //
            // Without this a path or query parameter typed as a described enum is parsed by
            // Enum.Parse against the C# member name, so `?genre=science-fiction` - the document's
            // own value - answers 400 while `?genre=ScienceFiction`, a name appearing nowhere in the
            // document, answers 200. The body and the response were always right; only parameters
            // spoke a second vocabulary.
            statements.Add(new CodeOutputComponent(
                $"foreach (var stringConverter in global::{registration.ResolverName}.StringConverters) " +
                "{ serviceCollection.AddSingleton(typeof(global::Hardened.Requests.Abstract.Serializer.IStringConverter), stringConverter); }"));
        }

        AddPublishedSpecs(statements, ordered);

        // The service-wide negotiation policy, from the entry point or from a description's root.
        var negotiation = ContentNegotiationRegistration.Statement(
            appModel.AttributeModels,
            ordered.FirstOrDefault(registration => registration.ContentNegotiation.Length > 0)
                ?.ContentNegotiation ?? "");

        if (negotiation != null) {
            statements.Add(new CodeOutputComponent(negotiation));
        }

        var declaredServiceNames = new HashSet<string>(handlers.Select(m => m.ControllerType.Name));

        foreach (var handlerInfo in handlerInfos) {
            if (handlerInfo == null) {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // The base-list entry naming a described service, rather than whichever came first.
            //
            // `class CatalogHandler : HandlerBase, ICatalogService` registered HandlerBase, because
            // C# puts a base class first and this read position 0. The build stayed clean and every
            // route on that service was dead. HOAG031 reports the case where nothing matches.
            var service = handlerInfo.ServiceInterface(declaredServiceNames);

            // The matching model, for the correctly namespaced interface type.
            var matchingModel = handlers.FirstOrDefault(m =>
                m.ControllerType.Name == (service ?? handlerInfo.InterfaceType).Name);

            var interfaceType = matchingModel?.ControllerType ?? service ?? handlerInfo.InterfaceType;

            statements.Add(new CodeOutputComponent(
                $"serviceCollection.AddTransient<{Global(interfaceType)}, {Global(handlerInfo.ImplementationType)}>()"));
        }

        return statements;
    }

    private static void AddPublishedSpecs(
        List<IOutputComponent> statements,
        IReadOnlyList<SpecRegistration> registrations) {
        foreach (var registration in registrations) {
            if (registration.PublishUrl.Length == 0) {
                continue;
            }

            statements.Add(new CodeOutputComponent(
                $"serviceCollection.AddSingleton<{Global(KnownTypes.Web.IWebExecutionRequestHandlerProvider)}>(" +
                "new global::Hardened.Web.Runtime.OpenApi.OpenApiDocumentProvider(global::" +
                registration.SpecificationTypeName + ".DocumentGZip, " +
                Quote(registration.PublishUrl) + ", global::" +
                registration.SpecificationTypeName + ".ContentType))"));

            if (registration.UiUrl.Length == 0) {
                continue;
            }

            statements.Add(CodeOutputComponent.Get(
                "global::DependencyModules.Runtime.ServiceCollectionExtensions.AddModule(" +
                "serviceCollection" +
                ", new global::Hardened.Web.Runtime.OpenApi.HardenedOpenApiUi { Path = " +
                Quote(registration.UiUrl) + ", DocumentPath = " +
                Quote(registration.PublishUrl) +
                (registration.UiEnvironments.Length == 0
                    ? ""
                    : ", Environments = " + Quote(registration.UiEnvironments)) + " })"));
        }
    }

    /// <summary>
    /// A globally qualified type name.
    /// </summary>
    /// <remarks>
    /// These statements are built without the emitted method's parameter in hand, so they name
    /// serviceCollection textually and qualify their own types. Composing them through
    /// InvokeGeneric on a CodeOutputComponent receiver does not work - it writes indexer syntax,
    /// and the generated file fails with CS0029 against System.Index.
    /// </remarks>
    private static string Global(ITypeDefinition type) {
        var builder = new StringBuilder();

        type.WriteTypeName(builder, TypeOutputMode.Global);

        return builder.ToString();
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
