using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Links;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Authorization;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;
using System.CodeDom.Compiler;

namespace Hardened.SourceGenerator.Web;

public static class RoutingTableGenerator {
    private static readonly IOutputComponent EmptyTokens =
        Property(KnownTypes.Requests.PathTokenCollection, "Empty");

    /// <summary>
    /// Whether this table matches without regard to case, from <c>[CaseInsensitiveRoutes]</c> on
    /// the entry point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static, and set at the top of each emit. Every method below would otherwise have to thread
    /// it through, including several that recurse - and the value is fixed for the whole of one
    /// table, since the attribute is on the entry point the table belongs to.
    /// </para>
    /// <para>
    /// A generator's source output runs one entry point at a time on one thread, so this is not
    /// shared state between concurrent emits in practice. It is a deliberate trade: the alternative
    /// is a parameter on a dozen private methods carrying one bool that is constant per call tree.
    /// </para>
    /// </remarks>
    [ThreadStatic]
    private static bool _caseInsensitive;

    /// <summary>
    /// The <c>[RouteConstraint]</c> methods the application declared, on the same terms as
    /// <see cref="_caseInsensitive"/>.
    /// </summary>
    [ThreadStatic]
    private static IReadOnlyList<RouteConstraintModel>? _constraints;

    /// <summary>
    /// The entry point's <c>[BasePath]</c>, on the same terms as <see cref="_constraints"/>.
    /// </summary>
    /// <remarks>
    /// Held here so the handler construction sites can compose it onto each route. A handler class
    /// is generated from its own declarations and cannot see the module's base path, so without
    /// this it reports the path it was declared with rather than the one it answers on.
    /// </remarks>
    [ThreadStatic]
    private static string? _basePath;

    public static void GenerateRoute(SourceProductionContext context,
        (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models,
        WebGeneratorOptions? options = null,
        IReadOnlyList<RouteConstraintModel>? constraints = null) {
        _constraints = constraints;

        options ??= WebGeneratorOptions.Default;

        // Handlers that were not generated must not be routed to. Routing to one would emit a
        // table referencing a handler class that does not exist - uncompilable output, which is
        // worse than the missing route. Skipped silently: WebExecutionHandlerCodeGenerator has
        // already reported each one, and it runs per handler rather than per table.
        var routable = models.Right
            .Where(handler => handler.UnresolvedParameter() == null)
            .ToList();

        // Before anything is emitted. An ambiguous pair still produces a table - one of the two
        // routes simply becomes unreachable for some values - so reporting is the only thing that
        // makes it visible, and the build fails on it by default.
        AmbiguousRouteDiagnostics.ReportAmbiguousRoutes(
            context,
            routable,
            GetBasePath(models.Left),
            AmbiguousRouteDiagnostics.Severity(options.AmbiguousRoutes));

        // Per handler, and before its dispatch is emitted. A case set the document cannot describe
        // unambiguously still produces a switch that compiles and runs - the ambiguity is in the
        // shipped contract rather than in the generated code, which is exactly why nothing else
        // would ever surface it.
        foreach (var handler in routable) {
            ResponseModelDiagnostics.ReportCaseSetFindings(
                context,
                handler.ControllerType.Name + "." + handler.HandlerMethod,
                handler.ResponseInformation.UnionDiagnostic);
        }

        // A scheme-shape attribute somewhere nothing reads it is a silent no-op - the build was
        // green, nothing published, and the author had no way to learn the working spelling.
        SecuritySchemeDiagnostics.ReportMisplacedSchemes(
            context,
            MisplacedSchemeFindings(models.Left, routable));

        // Before the document is written, because an unrecognised version has no answer to fall
        // back to - see OpenApiVersionDiagnostics.ReportUnknownVersion.
        var version = OpenApiVersionFacts.Parse(options.OpenApiVersion);

        if (version == null) {
            OpenApiVersionDiagnostics.ReportUnknownVersion(context, options.OpenApiVersion!);
        }

        var outputString = GenerateCSharpRouteFile(models.Left, routable, context.CancellationToken);

        var fileName = models.Left.EntryPointType.Name + ".Routing";

        context.AddSource(fileName, GeneratedSource.Header(outputString));

        var documentVersion = version ?? OpenApiVersionFacts.Default;

        // Only for an entry point that asked. The document used to be emitted unconditionally on
        // the grounds that it cost one string, which understated it - see OpenApiDocumentSource -
        // and an application that does not serve one now does not carry one.
        if (OpenApiDocumentFeature.Path(models.Left) is { } documentPath) {
            // An empty document is the one outcome that looks like success from every angle: the
            // build is clean, the route answers 200, and the reference page renders an API with no
            // operations. Said out loud, because the alternative is discovering it from a client
            // generator that produced nothing.
            if (routable.Count == 0) {
                OpenApiDocumentDiagnostics.ReportEmptyDocument(
                    context, models.Left.EntryPointType.Name, documentPath);
            }

            // A streamed response has no spelling before 3.2, so the document is emitted without one
            // and the handler is named rather than the omission being silent.
            if (!OpenApiVersionFacts.SupportsItemSchema(documentVersion)) {
                foreach (var handler in routable) {
                    if (handler.ResponseInformation.IsAsyncEnumerable) {
                        OpenApiVersionDiagnostics.ReportStreamNeedsItemSchema(
                            context,
                            handler.ControllerType.Name + "." + handler.HandlerMethod,
                            documentVersion);
                    }
                }
            }

            context.AddSource(models.Left.EntryPointType.Name + ".OpenApiDocument",
                GeneratedSource.Header(
                    OpenApiDocumentSource.Write(models.Left, routable, GetBasePath(models.Left), documentVersion)));
        }

        // From the same models the table came from, and unconditionally: links have no third-party
        // dependency, and the common case is an API with no views that still wants them for
        // Location headers - which are exactly the strings that rot.
        LinkGenerator.Generate(context, models.Left, routable, GetBasePath(models.Left));
    }
    
    /// <summary>
    /// Every misplaced scheme-shape attribute in this table's reach: each handler's findings, and
    /// the entry point's own attribute list - the module position, which no handler transform
    /// sees.
    /// </summary>
    private static IEnumerable<IReadOnlyList<string>> MisplacedSchemeFindings(
        EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> handlers) {
        foreach (var handler in handlers) {
            if (handler.MisplacedSchemeAttributes.Count > 0) {
                yield return handler.MisplacedSchemeAttributes;
            }
        }

        if (appModel.AttributeModels == null) {
            yield break;
        }

        foreach (var attribute in appModel.AttributeModels) {
            var name = attribute.TypeDefinition.Name;

            if (name.StartsWith("HttpAuthenticationScheme", System.StringComparison.Ordinal) ||
                name.StartsWith("ApiKeyAuthenticationScheme", System.StringComparison.Ordinal) ||
                name.StartsWith("OAuth2AuthenticationScheme", System.StringComparison.Ordinal)) {
                yield return new[] { appModel.EntryPointType.Name + "|" + name };
            }
        }
    }

    public static string GenerateCSharpRouteFile(EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> handlers, CancellationToken cancellationToken,
        RoutingTableOptions? options = null) {
        options ??= RoutingTableOptions.Default;

        if (options.Constraints != null) {
            _constraints = options.Constraints;
        }

        _caseInsensitive = IsCaseInsensitive(appModel);
        _basePath = options.UseEntryPointBasePath ? GetBasePath(appModel) : "";

        var applicationFile = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        // AddSingleton/AddTransient are extension methods, and an extension method is reachable
        // only through a using of its namespace - global:: cannot name one.
        applicationFile.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        CreateRoutingTable(appModel, handlers, applicationFile, cancellationToken, options);

        var outputContext = options.TypeOutputMode is { } mode
            ? new OutputContext(new OutputContextOptions { TypeOutputMode = mode })
            : new OutputContext();

        applicationFile.WriteOutput(outputContext);

        return outputContext.Output();
    }

    private static void CreateRoutingTable(EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> endPointModels,
        CSharpFileDefinition applicationFile, CancellationToken cancellationToken,
        RoutingTableOptions options) {
        cancellationToken.ThrowIfCancellationRequested();

        var appClass = applicationFile.AddClass(appModel.EntryPointType.Name);

        appClass.Modifiers |= ComponentModifier.Partial;

        var routingClass = appClass.AddClass(options.ClassName);

        CreateConstructor(routingClass);

        routingClass.Modifiers |= ComponentModifier.Private;

        routingClass.AddBaseType(KnownTypes.Web.IWebExecutionRequestHandlerProvider);

        if (options.ExcludeFromCodeCoverage) {
            routingClass.AddAttribute(
                TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));
        }

        ImplementHandlerMethod(appModel, routingClass, endPointModels, cancellationToken);

        var routingType = TypeDefinition.Get(appModel.EntryPointType.Namespace,
            appModel.EntryPointType.Name + "." + options.ClassName);

        // Before the DI method, which registers what this emits.
        var enums = EnumWireConverterEmitter.Collect(endPointModels);

        EnumWireConverterEmitter.Emit(appClass, enums);

        GenerateDependencyInjection(
            appClass, routingType, appModel, endPointModels, enums, cancellationToken, options);
    }

    private static void CreateConstructor(ClassDefinition appClass) {
        var field = appClass.AddField(typeof(IServiceProvider), "_rootServiceProvider");

        var constructor = appClass.AddConstructor();

        var parameter = constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");

        constructor.Assign(parameter).To(field.Instance);
    }

    private static void GenerateDependencyInjection(ClassDefinition classDefinition,
        ITypeDefinition routingTableType,
        EntryPointSelector.Model applicationModel, IReadOnlyList<RequestHandlerModel> webEndPointModels,
        IReadOnlyList<EnumVocabulary> enums,
        CancellationToken cancellationToken,
        RoutingTableOptions options) {
        cancellationToken.ThrowIfCancellationRequested();

        var templateField = classDefinition.AddField(typeof(int), options.DependencyFieldName);

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace(KnownTypes.Namespace.DependencyModules.Runtime.Helpers);
        templateField.InitializeValue = new CodeOutputComponent($"DependencyRegistry<{classDefinition.Name}>.Add({options.DependencyMethodName})");
        templateField.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"), $"nameof({options.DependencyMethodName})");

        var diMethod = classDefinition.AddMethod(options.DependencyMethodName);

        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] { KnownTypes.Web.IWebExecutionRequestHandlerProvider, routingTableType }));

        // The service-wide negotiation policy, from [ContentNegotiation] on the entry point. There
        // is no description to consult here; the spec-first table reads both. Same helper either
        // way, so an application says it once and means the same thing.
        var negotiation = Routing.ContentNegotiationRegistration.Statement(
            applicationModel.AttributeModels, "");

        if (negotiation != null) {
            diMethod.AddIndentedStatement(new CodeOutputComponent(negotiation));
        }

        if (options.RegisterControllerTypes) {
            var distinctControllers =
                webEndPointModels.Select(model => model.ControllerType).Distinct();

            foreach (var controllerType in distinctControllers) {
                cancellationToken.ThrowIfCancellationRequested();

                diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
                    new[] { controllerType }));
            }
        }

        RegisterLinks(diMethod, serviceCollection, applicationModel, webEndPointModels);

        RegisterOpenApiDocument(diMethod, serviceCollection, applicationModel, classDefinition);

        RegisterEnabledModules(diMethod, serviceCollection, applicationModel);

        RegisterAuthorizationPosture(diMethod, serviceCollection, applicationModel);

        RegisterEnumWireConverters(diMethod, serviceCollection, applicationModel, enums);

        Validation.ParameterValidatorRegistration.Write(
            diMethod, serviceCollection, webEndPointModels, cancellationToken);

        // Last, and already emitted by the caller. See RoutingTableOptions for why this is a list
        // of statements rather than a hook.
        foreach (var registration in options.AdditionalRegistrations) {
            diMethod.AddIndentedStatement(registration);
        }
    }

    /// <summary>
    /// The generated enum converters, as the serializers and the parameter binder consume them.
    /// </summary>
    /// <remarks>
    /// Two registrations for one vocabulary, because a value reaches the application by two routes
    /// that share nothing: a JSON body resolves through the type-info chain, and a path or query
    /// value is text the binder converts. An enum registered for only the first is one whose body
    /// accepts <c>"in-progress"</c> while <c>?priority=in-progress</c> is answered 400.
    /// </remarks>
    private static void RegisterEnumWireConverters(
        MethodDefinition diMethod,
        ParameterDefinition serviceCollection,
        EntryPointSelector.Model applicationModel,
        IReadOnlyList<EnumVocabulary> enums) {
        if (enums.Count == 0) {
            return;
        }

        var container = "global::" + applicationModel.EntryPointType.Namespace + "." +
                        applicationModel.EntryPointType.Name + "." +
                        EnumWireConverterEmitter.ContainerName;

        diMethod.AddIndentedStatement(new CodeOutputComponent(
            serviceCollection.Name +
            ".AddSingleton(typeof(global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver), " +
            container + ".Resolver.Instance)"));

        diMethod.AddIndentedStatement(new CodeOutputComponent(
            "foreach (var stringConverter in " + container + ".StringConverters) { " +
            serviceCollection.Name +
            ".AddSingleton(typeof(global::Hardened.Requests.Abstract.Serializer.IStringConverter), " +
            "stringConverter); }"));
    }

    /// <summary>
    /// Turns on the default-deny posture when the application asked for it.
    /// </summary>
    /// <remarks>
    /// The other half of <c>[RequireAuthorization]</c>. The diagnostic reports what the generator can
    /// see; this makes the runtime deny what it cannot - a handler from a referenced assembly was
    /// never analysed here, and is still guarded.
    /// </remarks>
    private static void RegisterAuthorizationPosture(
        MethodDefinition diMethod,
        ParameterDefinition serviceCollection,
        EntryPointSelector.Model applicationModel) {
        if (!RequireAuthorizationDiagnostics.IsRequired(applicationModel)) {
            return;
        }

        // The extension called statically, because generated code carries none of the consumer's
        // using directives - the same reason RegisterEnabledModules spells its call out in full.
        diMethod.AddIndentedStatement(CodeOutputComponent.Get(
            "global::Hardened.Requests.Runtime.Authorization.AuthorizationServiceCollectionExtensions" +
            ".RequireAuthorization(" + serviceCollection.Name + ")"));
    }

    /// <summary>
    /// The generated links type, so a handler can take it as a constructor parameter.
    /// </summary>
    /// <remarks>
    /// Transient rather than singleton: it holds an <c>ILinkContext</c>, and a host that learns the
    /// scheme and authority from the request it is answering registers a scoped one. Resolving a
    /// singleton that captured a scoped dependency is the failure eager container validation exists
    /// to catch.
    /// </remarks>
    private static void RegisterLinks(
        MethodDefinition diMethod,
        ParameterDefinition serviceCollection,
        EntryPointSelector.Model applicationModel,
        IReadOnlyList<RequestHandlerModel> webEndPointModels) {
        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
            new[] { LinkGenerator.LinksType(applicationModel) }));
    }

    /// <summary>
    /// Serves the generated document, when the entry point enabled it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted here rather than written by hand because it is the only place both halves exist: the
    /// document is a member of this entry point's own generated partial, and nothing outside this
    /// generator can name it. An attribute argument could not carry it either - it is
    /// <c>static readonly</c> rather than <c>const</c>, so it is not a compile-time constant.
    /// </para>
    /// <para>
    /// Registered ahead of the routing table, which is consulted first because providers are read in
    /// reverse registration order - so a route the application declares at this path shadows the
    /// document rather than colliding with it, matching how the health endpoints behave.
    /// </para>
    /// </remarks>
    private static void RegisterOpenApiDocument(
        MethodDefinition diMethod,
        ParameterDefinition serviceCollection,
        EntryPointSelector.Model applicationModel,
        ClassDefinition classDefinition) {
        var path = OpenApiDocumentFeature.Path(applicationModel);

        if (path == null) {
            return;
        }

        diMethod.AddIndentedStatement(
            serviceCollection.InvokeGeneric(
                "AddSingleton",
                new[] { KnownTypes.Web.IWebExecutionRequestHandlerProvider },
                CodeOutputComponent.Get(
                    // A factory rather than an instance. The provider builds its chain through
                    // ExecutionHelper - which is where conventions are applied and the global filter
                    // registry is asked for this handler's guard - and that needs the container.
                    "serviceProvider => new global::Hardened.Web.Runtime.OpenApi.OpenApiDocumentProvider(" +
                    "serviceProvider, " +
                    classDefinition.Name + "." + OpenApiDocumentSource.DocumentPropertyName +
                    ", \"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")")));
    }

    /// <summary>
    /// The registrations of any <c>[Enable&lt;T&gt;]</c> marker that is also a DependencyModules
    /// module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// So a feature that ships services and a generated type is one attribute rather than two.
    /// <c>AddModule</c> is DependencyModules' own entry point and goes through the module's
    /// <c>PopulateServiceCollection</c>, which composes its nested modules, decorators and features
    /// exactly as applying its attribute would.
    /// </para>
    /// <para>
    /// What differs is position: these arrive with the other generated registrations rather than
    /// where the attribute was written, which matters only for a module deliberately overriding a
    /// registration made by another.
    /// </para>
    /// </remarks>
    private static void RegisterEnabledModules(
        MethodDefinition diMethod,
        ParameterDefinition serviceCollection,
        EntryPointSelector.Model applicationModel) {
        foreach (var feature in applicationModel.EnabledFeatures) {
            if (!feature.IsDependencyModule) {
                continue;
            }

            // The extension called statically, because generated code carries none of the
            // consumer's using directives and DependencyModules.Runtime is not one of the few this
            // file imports.
            diMethod.AddIndentedStatement(CodeOutputComponent.Get(
                "global::DependencyModules.Runtime.ServiceCollectionExtensions.AddModule(" +
                serviceCollection.Name + ", new global::" +
                feature.MarkerType.Namespace + "." + feature.MarkerType.Name + "())"));
        }
    }

    private static void ImplementHandlerMethod(EntryPointSelector.Model appModel, ClassDefinition routingClass,
        IReadOnlyList<RequestHandlerModel> endPointModels, CancellationToken cancellationToken) {
        var handlerMethod = routingClass.AddMethod("GetExecutionRequestHandler");

        handlerMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());

        var context = handlerMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");

        // Two ways a request can name an operation, and an application may serve both.
        var dispatched = new List<RequestHandlerModel>();
        var routed = new List<RequestHandlerModel>();

        foreach (var model in endPointModels) {
            (model.Name.IsDispatched ? dispatched : routed).Add(model);
        }

        // Header dispatch first, and that order is a decision rather than an accident: an awsJson
        // service sends every operation to POST /, so a path tree consulted first would match that
        // route for one of them and answer the wrong handler for all the others.
        if (dispatched.Count > 0) {
            WriteDispatchTable(routingClass, handlerMethod, context, dispatched);
        }

        // Nothing left to route: no tree is built over an empty list.
        if (routed.Count == 0) {
            handlerMethod.Return(Null());

            return;
        }

        handlerMethod.Assign(context.Property("Request").Property("Path").Invoke("AsSpan")).ToVar("pathSpan");

        WriteRoutingTable(appModel, routingClass, handlerMethod, routed,
            context.Property("Request").Property("Method"), cancellationToken);
    }

    /// <summary>
    /// Selects a handler by an exact token carried in a request header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of RPC-style routing, and it is cheaper than the route tree rather than an
    /// addition to it: a switch over string literals is compiled to a computed hash and a jump, with
    /// no span slicing, no wildcard nodes, no ambiguity to diagnose and no verb fallback. The
    /// existing tree exists because a path has structure; a target does not.
    /// </para>
    /// <para>
    /// Grouped by header because the header is a property of the protocol and an application may
    /// serve two, which is the same reason the header is carried on the model rather than assumed to
    /// be X-Amz-Target.
    /// </para>
    /// <para>
    /// Without this, a dispatched model reaching this generator was routed by its path instead —
    /// and every awsJson operation declares POST /, so the switch came out with the same case label
    /// on each one. That is CS0152, so the failure was a build error rather than a wrong route.
    /// </para>
    /// </remarks>
    private static void WriteDispatchTable(
        ClassDefinition routingClass,
        MethodDefinition handlerMethod,
        ParameterDefinition context,
        IReadOnlyList<RequestHandlerModel> dispatched) {
        var headers = new List<string>();

        foreach (var model in dispatched) {
            if (!headers.Contains(model.Name.DispatchHeader!)) {
                headers.Add(model.Name.DispatchHeader!);
            }
        }

        for (var i = 0; i < headers.Count; i++) {
            var header = headers[i];
            var values = "dispatchValues" + (i == 0 ? "" : i.ToString());

            var ifHeader = handlerMethod.If(
                $"{context.Name}.Request.Headers.TryGetValue(\"{header}\", out var {values})");

            var switchBlock = ifHeader.Switch(
                new CodeOutputComponent($"{values}.ToString()") { Indented = false });

            foreach (var model in dispatched) {
                if (model.Name.DispatchHeader != header) {
                    continue;
                }

                var caseStatement = switchBlock.AddCase(QuoteString(model.Name.DispatchKey!));

                // The same lazily constructed singleton the route leaves use, so a dispatched
                // handler and a routed one are built and reused identically.
                var field = routingClass.AddField(
                    model.InvokeHandlerType.MakeNullable(),
                    "_field" + model.InvokeHandlerType.Name);

                var coalesceHandler = NullCoalesceEqual(field.Instance,
                    New(model.InvokeHandlerType, "_rootServiceProvider"));
                coalesceHandler.PrintParentheses = false;

                // No path tokens by construction: the route carries no template.
                caseStatement.Return(
                    New(KnownTypes.Web.RequestHandlerInfo, coalesceHandler, EmptyTokens));
            }
        }
    }

    private static void WriteRoutingTable(EntryPointSelector.Model appModel, ClassDefinition routingClass,
        MethodDefinition handlerMethod,
        IReadOnlyList<RequestHandlerModel> endPointModels,
        InstanceDefinition methodString, CancellationToken cancellationToken) {
        var routeNode = GetRoutingNodes(appModel, endPointModels, cancellationToken);

        var routeTestMethod = WriteRouteNode(routingClass, routeNode, 0, cancellationToken);

        handlerMethod.Return(Invoke(routeTestMethod, "pathSpan", 0, methodString));
    }

    private static string WriteRouteNode(ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> routeNode,
        int pathIndex, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var path = routeNode.Path;

        if (pathIndex > 0) {
            if (path.Length < 2) {
                path = "";
            }
            else {
                path = path.Substring(1);
            }
        }

        var routeMethodName = GetRouteMethodName(routingClass, path);

        var testMethod = routingClass.AddMethod(routeMethodName);
        testMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());

        var span = testMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
        var index = testMethod.AddParameter(typeof(int), "index");
        var methodString = testMethod.AddParameter(typeof(string), "methodString");

        var handler =
            testMethod.Assign(Null()).ToLocal(KnownTypes.Web.RequestHandlerInfo.MakeNullable(), "handlerInfo");

        BaseBlockDefinition block = testMethod;

        if (!string.IsNullOrEmpty(path)) {
            var pathIfStatement = CreatePathIfStatement(span, routeNode.Path, cancellationToken);

            block = testMethod.If(And(pathIfStatement));

            block.AddIndentedStatement("index += " + path.Length);
        }

        if (routeNode.LeafNodes.Count > 0) {
            ProcessLeafNodes(routingClass, routeNode, block, span, index, methodString, cancellationToken);
        }

        if (routeNode.ChildNodes.Count > 0) {
            ProcessChildNodes(routingClass, routeNode, block, span, index, methodString, handler, cancellationToken);
        }

        if (routeNode.WildCardNodes.Count > 0) {
            ProcessWildCardNodes(routingClass, routeNode, block, span, index, methodString, handler, cancellationToken);
        }

        testMethod.Return(handler);

        return routeMethodName;
    }

    private static void ProcessChildNodes(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        BaseBlockDefinition block,
        ParameterDefinition span,
        IOutputComponent index,
        ParameterDefinition methodString,
        InstanceDefinition handler,
        CancellationToken cancellationToken) {
        var childMethod = "";

        if (routeNode.ChildNodes.Count == 1) {
            childMethod = WriteRouteNode(routingClass, routeNode.ChildNodes.First(), 0, cancellationToken);
        }
        else {
            childMethod = WriteSwitchChildNode(routingClass, routeNode, cancellationToken);
        }

        block.Assign(Invoke(childMethod, span, index, methodString)).To(handler);
    }

    private static string WriteSwitchChildNode(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode, CancellationToken cancellationToken) {
        var switchMethodName = GetRouteMethodName(routingClass, routeNode.Path, "CaseStatement");

        var switchMethod = routingClass.AddMethod(switchMethodName);
        switchMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());
        var span = switchMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
        var index = switchMethod.AddParameter(typeof(int), "index");
        var methodString = switchMethod.AddParameter(typeof(string), "methodString");

        var ifStatement = switchMethod.If("charSpan.Length > index");

        var switchStatement = ifStatement.Switch("charSpan[index]");

        foreach (var childNode in routeNode.ChildNodes) {
            cancellationToken.ThrowIfCancellationRequested();

            var character = childNode.Path.First();

            // A second label only where the module asked for case-insensitive matching. It used to
            // be unconditional, which is half of what made every request compare each character
            // twice.
            if (_caseInsensitive) {
                var upperChar = char.ToUpperInvariant(character);

                if (upperChar != character) {
                    switchStatement.AddCase($"'{upperChar}'");
                }
            }

            var caseStatement = switchStatement.AddCase($"'{character}'");

            var newMethodName = WriteRouteNode(routingClass, childNode, 1, cancellationToken);

            var invoke = Invoke(newMethodName, span, "index + 1", methodString);

            caseStatement.Return(invoke);
        }

        switchMethod.Return(Null());

        return switchMethodName;
    }

    private static void ProcessWildCardNodes(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        BaseBlockDefinition block,
        ParameterDefinition span,
        IOutputComponent index,
        ParameterDefinition methodString,
        InstanceDefinition handler,
        CancellationToken cancellationToken) {
        var ifBlock = block.If("handlerInfo == null");

        var wildCardMethod = WriteWildCardMethod(routingClass, routeNode, cancellationToken);

        var invoke = Invoke(wildCardMethod, span, index, methodString);

        ifBlock.Assign(invoke).To(handler);
    }

    private static string WriteWildCardMethod(
        ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> routeNode,
        CancellationToken cancellationToken) {
        var methodName = GetRouteMethodName(routingClass, routeNode.Path, "WildCard");

        var wildCardMethod = routingClass.AddMethod(methodName);

        wildCardMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());
        var span = wildCardMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
        var index = wildCardMethod.AddParameter(typeof(int), "index");
        var methodString = wildCardMethod.AddParameter(typeof(string), "methodString");

        var handler =
            wildCardMethod.Assign(Null()).ToLocal(KnownTypes.Web.RequestHandlerInfo.MakeNullable(), "handlerInfo");

        var orderedList =
            routeNode.WildCardNodes.OrderByDescending(n => n.Path).ToList();

        for (var i = 0; i < orderedList.Count; i++) {
            cancellationToken.ThrowIfCancellationRequested();

            var wildCardNode = orderedList[i];
            BaseBlockDefinition currentBlock = wildCardMethod;

            if (i > 0) {
                currentBlock = wildCardMethod.If("handlerInfo == null");
            }

            var matchWildCardMethod = WriteWildCardMatchMethod(routingClass, wildCardNode, cancellationToken);

            currentBlock.Assign(Invoke(matchWildCardMethod, span, index, methodString)).To(handler);
        }

        wildCardMethod.Return(handler);

        return methodName;
    }

    private static string WriteWildCardMatchMethod(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> wildCardNode, CancellationToken cancellationToken) {
        var methodName = GetRouteMethodName(routingClass, wildCardNode.Path, "WildCardMatch");

        var wildCardMethod = routingClass.AddMethod(methodName);

        wildCardMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());
        var span = wildCardMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
        var index = wildCardMethod.AddParameter(typeof(int), "index");
        var methodString = wildCardMethod.AddParameter(typeof(string), "methodString");

        if (wildCardNode.ChildNodes.Count > 0) {
            GenerateWildCardChildMatch(
                routingClass, wildCardNode, wildCardMethod, methodString, span, index, cancellationToken);
        }

        if (wildCardNode.WildCardNodes.Count > 0) {
            GenerateWildCardChildMatch(
                routingClass, wildCardNode, wildCardMethod, methodString, span, index, cancellationToken);
        }

        GenerateWildCardLeafNode(routingClass, wildCardNode, wildCardMethod, methodString, span, index);

        return methodName;
    }

    private static void GenerateWildCardChildMatch(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> wildCardNode,
        MethodDefinition wildCardMethod,
        ParameterDefinition methodString,
        ParameterDefinition span,
        ParameterDefinition index,
        CancellationToken cancellationToken) {
        var handlerInfo = wildCardMethod.Assign(StaticCast(
            KnownTypes.Web.RequestHandlerInfo.MakeNullable(), Null())).ToVar("handlerInfo");

        var currentIndex = wildCardMethod.Assign(index).ToVar("currentIndex");

        // The scan looks for this node's separator and, when the rest of the route does not match
        // from there, tries the next occurrence. The retry is what resolves /files/{name}.{ext}
        // against "a.b.json": the first '.' leaves "b.json", which is not "json", so it takes the
        // second.
        //
        // Unbounded it also crosses '/', which for {name} is never right - a segment ends at the
        // first separator, and no later one makes a deeper path a legitimate match. It is why
        // /files/{name}/download used to answer /files/a/b/c/download with name = "a/b/c".
        // Bounding the scan to the current segment keeps the '.' case and removes the '/' case:
        // where the separator is '/' the limit is one past the first, so exactly one boundary is
        // tried, which is all there ever was to try.
        //
        // {*name} is unbounded, because taking the rest of the path is the whole point of it.
        //
        // Bounded is also less work: one vectorised IndexOf replaces walking the rest of the path a
        // character at a time whenever the match fails.
        IOutputComponent whileLimit = span.Property("Length");

        if (!wildCardNode.WildCardIsCatchAll) {
            wildCardMethod.Assign(
                    CodeOutputComponent.Get($"{span.Name}.Slice({index.Name}).IndexOf('/')"))
                .ToVar("segmentEnd");

            wildCardMethod.Assign(CodeOutputComponent.Get(
                    $"segmentEnd < 0 ? {span.Name}.Length : {index.Name} + segmentEnd + 1"))
                .ToVar("segmentLimit");

            whileLimit = CodeOutputComponent.Get("segmentLimit");
        }

        var whileBlock =
            wildCardMethod.While(LessThan(currentIndex, whileLimit));

        var pathCheck = CreatePathIfStatement(
            span, wildCardNode.Path, cancellationToken, currentIndex.Name);

        // The token has to have consumed something. Without this the scan accepts a boundary at the
        // position it started from, which is what let //items match /{id}/items with id bound to ""
        // - the same defect the terminal case had at the end of a path. First in the conjunction
        // because it is an integer compare and the rest is character work.
        pathCheck = new[] {
                (IOutputComponent)CodeOutputComponent.Get(
                    $"{currentIndex.Name} > {index.Name}")
            }
            .Concat(pathCheck).ToList();

        // The constraint is part of whether the token matched, not something checked afterwards:
        // failing it has to leave the scan free to try the next boundary, exactly as a literal
        // mismatch does.
        var constraintCheck = ConstraintTest(
            wildCardNode,
            $"{span.Name}.Slice({index.Name}, {currentIndex.Name} - {index.Name})");

        if (constraintCheck != null) {
            pathCheck = pathCheck.Concat(new[] { constraintCheck }).ToList();
        }

        var ifStatement = whileBlock.If(And(pathCheck));

        var currentPlusOne = Add(currentIndex, 1);

        if (wildCardNode.ChildNodes.Count > 0) {
            ProcessChildNodes(
                routingClass,
                wildCardNode,
                ifStatement,
                span,
                currentPlusOne,
                methodString,
                handlerInfo,
                cancellationToken);
        }

        if (wildCardNode.WildCardNodes.Count > 0) {
            ProcessWildCardNodes(
                routingClass,
                wildCardNode,
                ifStatement,
                span,
                currentPlusOne,
                methodString,
                handlerInfo,
                cancellationToken
            );
        }

        var matchIfHandlerBlock =
            ifStatement.If(NotEquals(handlerInfo, Null()));

        // Only a real match has somewhere to put the value. A path that matched under another verb
        // comes back as a RequestHandlerInfo too - non-null, with a null Handler - and it carries
        // PathTokenCollection.Empty, so writing a token into it throws IndexOutOfRangeException and
        // takes down a request that was on its way to an ordinary 405. That info is also a static
        // field shared by every leaf allowing the same verbs, so the write would be cross-request
        // mutation of shared state if the collection had been long enough to accept it.
        var realMatchBlock =
            matchIfHandlerBlock.If(NotEquals(handlerInfo.Property("Handler"), Null()));

        // The value is positional. Its name belongs to whichever route matched, which is
        // only known further down, so the collection was created with that route's names.
        realMatchBlock.AddIndentedStatement(
            handlerInfo.Property("PathTokens").Invoke(
                "SetValue",
                wildCardNode.WildCardDepth - 1,
                span.Invoke(
                    "Slice",
                    index,
                    Subtract(currentIndex, index)).Invoke("ToString")
            ));

        matchIfHandlerBlock.Return(handlerInfo);

        whileBlock.AddIndentedStatement(Increment(currentIndex));
    }

    private static void GenerateWildCardLeafNode(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> wildCardNode,
        MethodDefinition wildCardMethod, ParameterDefinition methodString, ParameterDefinition span,
        ParameterDefinition index) {
        if (wildCardNode.LeafNodes.Count > 0) {
            // A token that ends the route takes the rest of the path as its value. For {name} that
            // has to stop at the first separator, or the route matches paths deeper than it
            // declares: /users/{id} answered /users/42/anything/at/all with id = "42/anything/at/
            // all", and no route could be written that matched exactly one segment.
            //
            // {*name} is the form that does want the remainder, so it keeps the old behaviour.
            //
            // Read across the leaves rather than from one: they are the routes ending here, and a
            // node carrying both forms is a duplicate route either way. Permissive, so the
            // catch-all stays reachable - see RouteTreeNode.WildCardIsCatchAll.
            var catchAll = wildCardNode.LeafNodes.Any(
                leaf => RouteTokens.IsCatchAll(leaf.WildCardTokens, wildCardNode.WildCardDepth));

            // A token names at least one character. Nothing is left to name when the path ended on
            // the separator, so /collection/ is not a match for /collection/{id}. It used to bind
            // id to "" and come back 400 from the binder, which tells a client it addressed a real
            // endpoint incorrectly about a URL that addresses no endpoint at all. 404 is the
            // truthful answer, and it is what the routing guide's own rule implies: a token matches
            // exactly one segment, and the empty string after a trailing slash is not one.
            //
            // Catch-alls included. {*name} means the rest of the path, and /assets/ has no rest.
            wildCardMethod.If($"{span.Name}.Length <= {index.Name}").Return(Null());

            if (!catchAll) {
                wildCardMethod.If($"{span.Name}.Slice({index.Name}).IndexOf('/') >= 0").Return(Null());
            }

            // A value that fails the constraint is not a match at all, so the answer is null rather
            // than a 405: there is no resource at that URL, which is the whole point of writing
            // {id:int} instead of letting the binder answer 400.
            var constraintTest = ConstraintTest(wildCardNode, $"{span.Name}.Slice({index.Name})", negate: true);

            if (constraintTest != null) {
                wildCardMethod.If(constraintTest).Return(Null());
            }

            var switchBlock = wildCardMethod.Switch(methodString);

            foreach (var leafNode in wildCardNode.LeafNodes) {
                if (RouteMethods.AddsHeadFallThrough(wildCardNode.LeafNodes, leafNode)) {
                    switchBlock.AddCase(QuoteString(RouteMethods.Head));
                }

                var caseStatement = switchBlock.AddCase(QuoteString(leafNode.Method));

                var field =
                    routingClass.AddField(leafNode.Value.InvokeHandlerType.MakeNullable(),
                        "_field" + leafNode.Value.InvokeHandlerType.Name);

                var coalesceHandler = NullCoalesceEqual(field.Instance,
                    NewHandler(leafNode));

                coalesceHandler.PrintParentheses = false;

                IOutputComponent pathTokensCollection =
                    New(KnownTypes.Requests.PathTokenCollection,
                        wildCardNode.WildCardDepth,
                        PathTokenNamesField(routingClass, leafNode),
                        span.Invoke("Slice", index).Invoke("ToString")
                    );

                caseStatement.Return(
                    New(KnownTypes.Web.RequestHandlerInfo,
                        coalesceHandler,
                        pathTokensCollection));
            }

            switchBlock.AddDefault().Return(MethodNotAllowed(routingClass, wildCardNode.LeafNodes));
        }
        else {
            wildCardMethod.Return(Null());
        }
    }

    private static void ProcessLeafNodes(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        BaseBlockDefinition block,
        ParameterDefinition span,
        ParameterDefinition index,
        ParameterDefinition methodString, CancellationToken cancellationToken) {
        var ifLengthMatch = block.If("charSpan.Length == index");

        var switchStatement = ifLengthMatch.Switch(methodString);

        foreach (var leafNode in routeNode.LeafNodes) {
            cancellationToken.ThrowIfCancellationRequested();

            if (RouteMethods.AddsHeadFallThrough(routeNode.LeafNodes, leafNode)) {
                switchStatement.AddCase(QuoteString(RouteMethods.Head));
            }

            var caseStatement = switchStatement.AddCase(QuoteString(leafNode.Method));

            // A route with no tokens resolves to the same RequestHandlerInfo on every request:
            // the handler is already cached, and the token collection is the shared empty one.
            // Caching the record itself rather than rebuilding it drops an allocation per
            // request and collapses the leaf to a single field read.
            if (routeNode.WildCardDepth == 0) {
                var infoField = routingClass.AddField(
                    KnownTypes.Web.RequestHandlerInfo.MakeNullable(),
                    "_info" + leafNode.Value.InvokeHandlerType.Name);

                var cachedInfo = NullCoalesceEqual(infoField.Instance,
                    New(
                        KnownTypes.Web.RequestHandlerInfo,
                        NewHandler(leafNode),
                        EmptyTokens));

                cachedInfo.PrintParentheses = false;

                caseStatement.Return(cachedInfo);

                continue;
            }

            var field =
                routingClass.AddField(leafNode.Value.InvokeHandlerType.MakeNullable(),
                    "_field" + leafNode.Value.InvokeHandlerType.Name);

            var coalesceHandler = NullCoalesceEqual(field.Instance,
                NewHandler(leafNode));

            coalesceHandler.PrintParentheses = false;

            // Token values are per request, so only the handler can be reused here.
            var pathTokensCollection = New(KnownTypes.Requests.PathTokenCollection,
                routeNode.WildCardDepth,
                PathTokenNamesField(routingClass, leafNode));

            caseStatement.Return(
                New(
                    KnownTypes.Web.RequestHandlerInfo,
                    coalesceHandler,
                    pathTokensCollection));
        }

        switchStatement.AddDefault().Return(MethodNotAllowed(routingClass, routeNode.LeafNodes));
    }

    /// <summary>
    /// The result for a path this leaf matched under a verb it does not answer.
    /// </summary>
    /// <remarks>
    /// A static field per distinct verb set: it carries no per-request state - no handler, and the
    /// shared empty token collection - so rebuilding one per rejected request would allocate for
    /// the case least worth allocating for. Shared across leaves that allow the same verbs, which
    /// most of an application's do.
    /// </remarks>
    private static IOutputComponent MethodNotAllowed<T>(
        ClassDefinition routingClass, IReadOnlyList<RouteTreeLeafNode<T>> leaves) {
        var allow = RouteMethods.Allow(leaves);
        var fieldName = "_methodNotAllowed" + allow.Replace(", ", "");

        var existing = routingClass.Fields.FirstOrDefault(field => field.Name == fieldName);

        if (existing != null) {
            return existing.Instance;
        }

        var newField = routingClass.AddField(KnownTypes.Web.RequestHandlerInfo, fieldName);

        newField.Modifiers |= ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        newField.InitializeValue = new CodeOutputComponent(
            "global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed(\"" + allow + "\")");

        return newField.Instance;
    }

    /// <summary>
    /// The call that tests this node's constraint against <paramref name="value"/>, or null when
    /// the token declares none.
    /// </summary>
    private static IOutputComponent? ConstraintTest<T>(
        RouteTreeNode<T> node, string value, bool negate = false) {
        var constraint = node.WildCardConstraint;

        if (string.IsNullOrEmpty(constraint)) {
            return null;
        }

        var terms = RouteConstraintFacts.Terms(constraint!);

        if (terms == null) {
            return null;
        }

        // A chain is a conjunction, short-circuiting left to right, so the narrower term is written
        // first and the wider ones never run on a value the first has already rejected.
        var calls = new List<string>();

        foreach (var term in terms) {
            var test = RouteConstraintFacts.Call(term) ?? Custom(term.Name);

            // Null only for a name no built-in and no [RouteConstraint] declares, or one used at an
            // arity it does not have - both already reported by RouteTokenDiagnostics. Emitting a
            // call to nothing would bury that under a CS0103.
            if (test == null) {
                return null;
            }

            calls.Add(test + "(" + value + Arguments(term) + ")");
        }

        if (calls.Count == 0) {
            return null;
        }

        var conjunction = string.Join(" && ", calls);

        // Parentheses only where they change the meaning: !A && B negates the first term alone, and
        // a bare conjunction joined into a larger one binds wrong. A single term needs neither, and
        // the single term is the overwhelmingly common route - so the generated code stays the shape
        // it was before chains existed.
        if (calls.Count == 1) {
            return CodeOutputComponent.Get((negate ? "!" : "") + conjunction);
        }

        return CodeOutputComponent.Get((negate ? "!" : "") + "(" + conjunction + ")");
    }

    /// <summary>The literal arguments a term carries, ready to append after the span.</summary>
    private static string Arguments(RouteConstraintFacts.Term term) {
        if (term.Arguments.Count == 0) {
            return "";
        }

        var builder = new StringBuilder();

        foreach (var argument in term.Arguments) {
            builder.Append(", ").Append(argument.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The call a <c>[RouteConstraint]</c> declares for this name, or null.
    /// </summary>
    /// <remarks>
    /// A declaration with the wrong signature is skipped rather than called: emitting a call to a
    /// method that is not a <c>static bool(ReadOnlySpan&lt;char&gt;)</c> would bury the diagnostic
    /// that says so under a compiler error in generated code.
    /// </remarks>
    private static string? Custom(string constraint) {
        if (_constraints == null) {
            return null;
        }

        foreach (var declared in _constraints) {
            if (declared.SignatureIsValid && string.Equals(declared.Name, constraint, StringComparison.Ordinal)) {
                return declared.Call;
            }
        }

        return null;
    }

    private static IReadOnlyList<IOutputComponent> CreatePathIfStatement(
        ParameterDefinition span,
        string routeNodePath,
        CancellationToken cancellationToken,
        string indexName = "index") {
        var returnList = new List<IOutputComponent>();

        returnList.Add(GreaterThanOrEquals(span.Property("Length"), indexName + " + " + routeNodePath.Length));

        int index = 0;
        foreach (var pathChar in routeNodePath) {
            cancellationToken.ThrowIfCancellationRequested();

            var equalStatement = EqualsStatement($"{span.Name}[{indexName} + {index}]", "'" + pathChar + "'");

            // One comparison per character, unless the module asked for case-insensitive matching.
            // The second was emitted for every letter of every literal in every route, and ran on
            // every request.
            if (_caseInsensitive) {
                var upperChar = char.ToUpperInvariant(pathChar);

                if (upperChar != pathChar) {
                    returnList.Add(Or(
                        equalStatement,
                        EqualsStatement($"{span.Name}[{indexName} + {index}]", "'" + upperChar + "'")));

                    index++;

                    continue;
                }
            }

            returnList.Add(equalStatement);

            index++;
        }

        return returnList;
    }

    private static string GetRouteMethodName(ClassDefinition routingClass,
        string path, string? postfix = null) {
        if (string.IsNullOrEmpty(path)) {
            path = "NoPath";
        }

        var baseName = "TestPath_" +
                       path.Replace("/", "Slash").Replace("-", "Dash").Replace(".", "Period").Replace("%", "Per");

        var testMethodName = baseName + postfix;
        var count = 1;
        while (routingClass.Methods.Any(m => m.Name == testMethodName)) {
            testMethodName = baseName + (++count);
        }

        return testMethodName;
    }

    private static RouteTreeNode<RequestHandlerModel> GetRoutingNodes(EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> endPointModels, CancellationToken cancellationToken) {
        var generator = new RouteTreeGenerator<RequestHandlerModel>(cancellationToken);

        var basePath = GetBasePath(appModel);
        
        return generator.GenerateTree(endPointModels.Select(
            m => new RouteTreeGenerator<RequestHandlerModel>.Entry(
                RoutePath.Combine(basePath, m.Name.Path),
                m.Name.Method,
                m,
                _caseInsensitive
            )).ToList());
    }

    /// <summary>
    /// Whether the entry point asked for case-insensitive matching, which every application used to
    /// get whether it wanted it or not.
    /// </summary>
    private static bool IsCaseInsensitive(EntryPointSelector.Model appModel) =>
        appModel.AttributeModels != null &&
        appModel.AttributeModels.Any(model =>
            model.TypeDefinition.Name.StartsWith("CaseInsensitiveRoutes", StringComparison.Ordinal));

    /// <summary>
    /// Constructs the handler, telling it the path it is being routed at.
    /// </summary>
    /// <remarks>
    /// The path is composed here, with the same <see cref="RoutePath.Combine"/> that built the
    /// route tree, rather than at run time from the two parts - so there is no second
    /// implementation of the base-path rules to drift from this one. The argument is omitted
    /// entirely when the entry point declares no base path, which keeps the generated table
    /// unchanged for every application that has none.
    /// </remarks>
    private static IOutputComponent NewHandler(RouteTreeLeafNode<RequestHandlerModel> leafNode) {
        if (string.IsNullOrEmpty(_basePath)) {
            return New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider");
        }

        return New(
            leafNode.Value.InvokeHandlerType,
            "_rootServiceProvider",
            QuoteString(RoutePath.Combine(_basePath, leafNode.Value.Name.Path)));
    }

    private static string GetBasePath(EntryPointSelector.Model appModel) {
        if (appModel.AttributeModels != null) {
            var basePathAttribute = appModel.AttributeModels.FirstOrDefault(model => model.TypeDefinition.Name.StartsWith("BasePath"));

            if (basePathAttribute != null) {
                var basePath = basePathAttribute.Arguments.Split(',').First();

                return basePath.Trim('"');
            }
        }

        return "";
    }

    /// <summary>
    /// Emits a static readonly array of the route's path token names and returns a reference
    /// to it. The names are compile-time constants belonging to the route, so one shared
    /// array serves every request rather than allocating a PathToken per token per request.
    /// </summary>
    private static IOutputComponent PathTokenNamesField(
        ClassDefinition routingClass, RouteTreeLeafNode<RequestHandlerModel> leafNode) {
        var fieldName = "_pathTokenNames" + leafNode.Value.InvokeHandlerType.Name;

        var existing = routingClass.Fields.FirstOrDefault(f => f.Name == fieldName);

        if (existing != null) {
            return existing.Instance;
        }

        var field = routingClass.AddField(typeof(string).MakeArrayType(), fieldName);

        field.Modifiers |= ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

        // Without the marker: {*path} binds to a parameter called path. The asterisk says how much
        // of the path to take, not what to call it.
        var names = string.Join(", ",
            leafNode.WildCardTokens.Select(t => "\"" + RouteTokens.Name(t) + "\""));

        field.InitializeValue = new CodeOutputComponent("new string[] { " + names + " }");

        return field.Instance;
    }

}