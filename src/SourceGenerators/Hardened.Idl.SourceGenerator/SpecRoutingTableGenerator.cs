using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.Idl.Models;
using Hardened.SourceGenerator.Links;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;

namespace Hardened.Idl.SourceGenerator;

internal static class SpecRoutingTableGenerator {
    private static readonly IOutputComponent EmptyTokens =
        Property(KnownTypes.Requests.PathTokenCollection, "Empty");

    /// <summary>
    /// Whether this table matches without regard to case, from <c>[CaseInsensitiveRoutes]</c> on
    /// the entry point. Set at the top of each emit, for the reason the attribute-routed generator
    /// does the same.
    /// </summary>
    [ThreadStatic]
    private static bool _caseInsensitive;

    public static void GenerateRoute(
        SourceProductionContext context,
        (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models,
        ImmutableArray<HandlerInfo?> handlerInfos,
        ImmutableArray<string> jsonTypeInfoResolvers,
        bool excludeFromCoverage = false) {
        var outputString = GenerateCSharpRouteFile(models.Left, models.Right, handlerInfos, jsonTypeInfoResolvers, context.CancellationToken, excludeFromCoverage);
        var fileName = models.Left.EntryPointType.Name + ".SpecRouting";
        context.AddSource(fileName, outputString);

        // The same links an attribute-routed application gets, from the same models. A document
        // generates the routes, so a link built from one is checked against the document rather
        // than against a hand-written route.
        LinkGenerator.Generate(context, models.Left, models.Right, "");
    }

    public static string GenerateCSharpRouteFile(
        EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> handlers,
        ImmutableArray<HandlerInfo?> handlerInfos,
        ImmutableArray<string> jsonTypeInfoResolvers,
        CancellationToken cancellationToken,
        bool excludeFromCoverage = false) {
        _caseInsensitive = appModel.AttributeModels != null &&
                           appModel.AttributeModels.Any(model =>
                               model.TypeDefinition.Name.StartsWith(
                                   "CaseInsensitiveRoutes", StringComparison.Ordinal));

        var applicationFile = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        CreateRoutingTable(appModel, handlers, handlerInfos, jsonTypeInfoResolvers, applicationFile, cancellationToken, excludeFromCoverage);

        var outputContext = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });
        applicationFile.WriteOutput(outputContext);
        return outputContext.Output();
    }

    private static void CreateRoutingTable(
        EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> endPointModels,
        ImmutableArray<HandlerInfo?> handlerInfos,
        ImmutableArray<string> jsonTypeInfoResolvers,
        CSharpFileDefinition applicationFile,
        CancellationToken cancellationToken,
        bool excludeFromCoverage = false) {
        cancellationToken.ThrowIfCancellationRequested();

        var appClass = applicationFile.AddClass(appModel.EntryPointType.Name);
        appClass.Modifiers |= ComponentModifier.Partial;

        var routingClass = appClass.AddClass("SpecRoutingTable");
        routingClass.Modifiers |= ComponentModifier.Private;
        routingClass.AddBaseType(KnownTypes.Web.IWebExecutionRequestHandlerProvider);

        if (excludeFromCoverage) {
            routingClass.AddAttribute(
                TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));
        }

        CreateConstructor(routingClass);
        ImplementHandlerMethod(appModel, routingClass, endPointModels, cancellationToken);

        var routingType = TypeDefinition.Get(
            appModel.EntryPointType.Namespace,
            appModel.EntryPointType.Name + ".SpecRoutingTable");

        GenerateDependencyInjection(appClass, routingType, appModel, endPointModels, handlerInfos, jsonTypeInfoResolvers, cancellationToken);
    }

    private static void CreateConstructor(ClassDefinition appClass) {
        var field = appClass.AddField(typeof(IServiceProvider), "_rootServiceProvider");
        var constructor = appClass.AddConstructor();
        var parameter = constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        constructor.Assign(parameter).To(field.Instance);
    }

    private static void GenerateDependencyInjection(
        ClassDefinition classDefinition,
        ITypeDefinition routingTableType,
        EntryPointSelector.Model applicationModel,
        IReadOnlyList<RequestHandlerModel> webEndPointModels,
        ImmutableArray<HandlerInfo?> handlerInfos,
        ImmutableArray<string> jsonTypeInfoResolvers,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var templateField = classDefinition.AddField(typeof(int), "_openApiRoutingTableDependencies");
        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace(KnownTypes.Namespace.DependencyModules.Runtime.Helpers);
        templateField.InitializeValue = new CodeOutputComponent($"DependencyRegistry<{classDefinition.Name}>.Add(SpecRoutingTableDI)");
        templateField.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"), "nameof(SpecRoutingTableDI)");

        var diMethod = classDefinition.AddMethod("SpecRoutingTableDI");
        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] { KnownTypes.Web.IWebExecutionRequestHandlerProvider, routingTableType }));

        // Register the OpenAPI-generated JSON type info resolvers for AOT serialization.
        //
        // One per spec file, by the name the build task emitted and recorded in the model. This used
        // to derive a single "{RootNamespace}.Models.OpenApiJsonTypeInfoResolver" from the first
        // handler's namespace, which meant two spec files in one project emitted two classes of that
        // one name and the project did not compile - finding 3.1. Ordered so the emitted table does
        // not reshuffle between builds.
        foreach (var resolver in jsonTypeInfoResolvers.OrderBy(name => name, StringComparer.Ordinal)) {
            diMethod.AddIndentedStatement(new CodeOutputComponent(
                $"serviceCollection.AddSingleton(typeof(global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver), global::{resolver}.Instance)"));
        }

        // Register interface → implementation mappings from [Handler] classes
        foreach (var handlerInfo in handlerInfos) {
            if (handlerInfo == null) continue;
            cancellationToken.ThrowIfCancellationRequested();

            // Find the matching model to get the correctly-namespaced interface type
            var matchingModel = webEndPointModels.FirstOrDefault(m =>
                m.ControllerType.Name == handlerInfo.InterfaceType.Name);

            var interfaceType = matchingModel?.ControllerType ?? handlerInfo.InterfaceType;

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
                new[] { interfaceType, handlerInfo.ImplementationType }));
        }

        // The generated links type, so a handler can take it as a constructor parameter. Transient
        // rather than singleton: it holds an ILinkContext, and a host that learns the scheme and
        // authority from the request it is answering registers a scoped one.
        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
            new[] { LinkGenerator.LinksType(applicationModel) }));
    }

    private static void ImplementHandlerMethod(
        EntryPointSelector.Model appModel,
        ClassDefinition routingClass,
        IReadOnlyList<RequestHandlerModel> endPointModels,
        CancellationToken cancellationToken) {
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

    private static void WriteRoutingTable(
        EntryPointSelector.Model appModel,
        ClassDefinition routingClass,
        MethodDefinition handlerMethod,
        IReadOnlyList<RequestHandlerModel> endPointModels,
        InstanceDefinition methodString,
        CancellationToken cancellationToken) {
        var routeNode = GetRoutingNodes(appModel, endPointModels, cancellationToken);
        var routeTestMethod = WriteRouteNode(routingClass, routeNode, 0, cancellationToken);
        handlerMethod.Return(Invoke(routeTestMethod, "pathSpan", 0, methodString));
    }

    private static string WriteRouteNode(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        int pathIndex,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var path = routeNode.Path;
        if (pathIndex > 0) {
            path = path.Length < 2 ? "" : path.Substring(1);
        }

        var routeMethodName = GetRouteMethodName(routingClass, path);
        var testMethod = routingClass.AddMethod(routeMethodName);
        testMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());

        var span = testMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
        var index = testMethod.AddParameter(typeof(int), "index");
        var methodString = testMethod.AddParameter(typeof(string), "methodString");

        var handler = testMethod.Assign(Null())
            .ToLocal(KnownTypes.Web.RequestHandlerInfo.MakeNullable(), "handlerInfo");

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

    private static void ProcessChildNodes(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        BaseBlockDefinition block,
        ParameterDefinition span,
        IOutputComponent index,
        ParameterDefinition methodString,
        InstanceDefinition handler,
        CancellationToken cancellationToken) {
        string childMethod;

        if (routeNode.ChildNodes.Count == 1) {
            childMethod = WriteRouteNode(routingClass, routeNode.ChildNodes.First(), 0, cancellationToken);
        } else {
            childMethod = WriteSwitchChildNode(routingClass, routeNode, cancellationToken);
        }

        block.Assign(Invoke(childMethod, span, index, methodString)).To(handler);
    }

    private static string WriteSwitchChildNode(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        CancellationToken cancellationToken) {
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

            if (_caseInsensitive) {
                var upperChar = char.ToUpperInvariant(character);

                if (upperChar != character) {
                    switchStatement.AddCase($"'{upperChar}'");
                }
            }

            var caseStatement = switchStatement.AddCase($"'{character}'");
            var newMethodName = WriteRouteNode(routingClass, childNode, 1, cancellationToken);
            caseStatement.Return(Invoke(newMethodName, span, "index + 1", methodString));
        }

        switchMethod.Return(Null());
        return switchMethodName;
    }

    private static void ProcessWildCardNodes(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        BaseBlockDefinition block,
        ParameterDefinition span,
        IOutputComponent index,
        ParameterDefinition methodString,
        InstanceDefinition handler,
        CancellationToken cancellationToken) {
        var ifBlock = block.If("handlerInfo == null");
        var wildCardMethod = WriteWildCardMethod(routingClass, routeNode, cancellationToken);
        ifBlock.Assign(Invoke(wildCardMethod, span, index, methodString)).To(handler);
    }

    private static string WriteWildCardMethod(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        CancellationToken cancellationToken) {
        var methodName = GetRouteMethodName(routingClass, routeNode.Path, "WildCard");
        var wildCardMethod = routingClass.AddMethod(methodName);
        wildCardMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());

        var span = wildCardMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
        var index = wildCardMethod.AddParameter(typeof(int), "index");
        var methodString = wildCardMethod.AddParameter(typeof(string), "methodString");

        var handler = wildCardMethod.Assign(Null())
            .ToLocal(KnownTypes.Web.RequestHandlerInfo.MakeNullable(), "handlerInfo");

        var orderedList = routeNode.WildCardNodes.OrderByDescending(n => n.Path).ToList();

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

    private static string WriteWildCardMatchMethod(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> wildCardNode,
        CancellationToken cancellationToken) {
        var methodName = GetRouteMethodName(routingClass, wildCardNode.Path, "WildCardMatch");
        var wildCardMethod = routingClass.AddMethod(methodName);
        wildCardMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());

        var span = wildCardMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
        var index = wildCardMethod.AddParameter(typeof(int), "index");
        var methodString = wildCardMethod.AddParameter(typeof(string), "methodString");

        if (wildCardNode.ChildNodes.Count > 0 || wildCardNode.WildCardNodes.Count > 0) {
            GenerateWildCardChildMatch(routingClass, wildCardNode, wildCardMethod, methodString, span, index, cancellationToken);
        }

        GenerateWildCardLeafNode(routingClass, wildCardNode, wildCardMethod, methodString, span, index);

        return methodName;
    }

    private static void GenerateWildCardChildMatch(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> wildCardNode,
        MethodDefinition wildCardMethod,
        ParameterDefinition methodString,
        ParameterDefinition span,
        ParameterDefinition index,
        CancellationToken cancellationToken) {
        var handlerInfo = wildCardMethod.Assign(StaticCast(
            KnownTypes.Web.RequestHandlerInfo.MakeNullable(), Null())).ToVar("handlerInfo");

        var currentIndex = wildCardMethod.Assign(index).ToVar("currentIndex");

        // Bounded to the current segment, as the attribute-routed table is. Unbounded, the scan
        // retries at every later separator when the rest of the route does not match, so a token
        // could span segments and /files/{name}/download answered /files/a/b/c/download.
        //
        // There is no catch-all here. An OpenAPI path template cannot express one - a template
        // expression is a parameter name and nothing more - so every token compiled from a
        // document is a single segment, and this needs no opt-out.
        wildCardMethod.Assign(CodeOutputComponent.Get($"{span.Name}.Slice({index.Name}).IndexOf('/')"))
            .ToVar("segmentEnd");

        wildCardMethod.Assign(CodeOutputComponent.Get(
                $"segmentEnd < 0 ? {span.Name}.Length : {index.Name} + segmentEnd + 1"))
            .ToVar("segmentLimit");

        var whileBlock =
            wildCardMethod.While(LessThan(currentIndex, CodeOutputComponent.Get("segmentLimit")));

        var pathCheck = CreatePathIfStatement(span, wildCardNode.Path, cancellationToken, currentIndex.Name);

        // The token has to have consumed something. Without this the scan accepts a boundary at the
        // position it started from, which is what let //items match /{id}/items with id bound to ""
        // - the same defect the terminal case had at the end of a path. First in the conjunction
        // because it is an integer compare and the rest is character work.
        pathCheck = new[] {
                (IOutputComponent)CodeOutputComponent.Get($"{currentIndex.Name} > {index.Name}")
            }
            .Concat(pathCheck).ToList();

        // Part of whether the token matched, not something checked afterwards: failing it has to
        // leave the scan free to try the next boundary, exactly as a literal mismatch does.
        var constraintCheck = ConstraintTest(
            wildCardNode, $"{span.Name}.Slice({index.Name}, {currentIndex.Name} - {index.Name})");

        if (constraintCheck != null) {
            pathCheck = pathCheck.Concat(new[] { constraintCheck }).ToList();
        }

        var ifStatement = whileBlock.If(And(pathCheck));
        var currentPlusOne = Add(currentIndex, 1);

        if (wildCardNode.ChildNodes.Count > 0) {
            ProcessChildNodes(routingClass, wildCardNode, ifStatement, span, currentPlusOne,
                methodString, handlerInfo, cancellationToken);
        }

        if (wildCardNode.WildCardNodes.Count > 0) {
            ProcessWildCardNodes(routingClass, wildCardNode, ifStatement, span, currentPlusOne,
                methodString, handlerInfo, cancellationToken);
        }

        var matchIfHandlerBlock = ifStatement.If(NotEquals(handlerInfo, Null()));

        var newPathToken = New(KnownTypes.Requests.PathToken,
            QuoteString(wildCardNode.WildCardToken!),
            span.Invoke("Slice", index, Subtract(currentIndex, index)).Invoke("ToString"));

        matchIfHandlerBlock.AddIndentedStatement(
            handlerInfo.Property("PathTokens").Invoke("Set", wildCardNode.WildCardDepth - 1, newPathToken));

        matchIfHandlerBlock.Return(handlerInfo);
        whileBlock.AddIndentedStatement(Increment(currentIndex));
    }

    /// <summary>
    /// The call that tests this node's constraint against <paramref name="value"/>, or null when the
    /// token declares none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The twin of <c>RoutingTableGenerator.ConstraintTest</c>, and it has to stay one. The route
    /// tree is shared source, so a node built from a document carries <c>WildCardConstraint</c>
    /// exactly as one built from an attribute does - and until this existed, the spec-first table
    /// read that property nowhere. A constrained token compiled, routed, and constrained nothing,
    /// which is the state <c>{id:int}</c> was in before any of this.
    /// </para>
    /// <para>
    /// Built-ins only. <c>SpecSourceGenerator</c> does not collect <c>[RouteConstraint]</c>
    /// declarations, so a custom name is not resolvable here - but it is not silent either:
    /// <c>RouteTokenDiagnostics</c> runs over a document's paths too, so an undeclared name is
    /// <c>HRDR002</c> at build time rather than a route that quietly matches everything.
    /// </para>
    /// </remarks>
    private static IOutputComponent? ConstraintTest(
        RouteTreeNode<RequestHandlerModel> node, string value, bool negate = false) {
        var constraint = node.WildCardConstraint;

        if (string.IsNullOrEmpty(constraint)) {
            return null;
        }

        var terms = RouteConstraintFacts.Terms(constraint!);

        if (terms == null) {
            return null;
        }

        // A chain is a conjunction, short-circuiting left to right.
        var calls = new List<string>();

        foreach (var term in terms) {
            var test = RouteConstraintFacts.Call(term);

            // Null only for a name no built-in has, or one used at an arity it does not have - both
            // already reported. Emitting a call to nothing would bury that under a CS0103.
            if (test == null) {
                return null;
            }

            var arguments = new StringBuilder();

            foreach (var argument in term.Arguments) {
                arguments.Append(", ").Append(argument.ToString(CultureInfo.InvariantCulture));
            }

            calls.Add(test + "(" + value + arguments + ")");
        }

        if (calls.Count == 0) {
            return null;
        }

        var conjunction = string.Join(" && ", calls);

        // Parentheses only where they change the meaning, as in the attribute-routed twin.
        if (calls.Count == 1) {
            return CodeOutputComponent.Get((negate ? "!" : "") + conjunction);
        }

        return CodeOutputComponent.Get((negate ? "!" : "") + "(" + conjunction + ")");
    }

    private static void GenerateWildCardLeafNode(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> wildCardNode,
        MethodDefinition wildCardMethod,
        ParameterDefinition methodString,
        ParameterDefinition span,
        ParameterDefinition index) {
        if (wildCardNode.LeafNodes.Count > 0) {
            // A token names at least one character. Nothing is left to name when the path ended on
            // the separator, so /collection/ is not a match for /collection/{id}. It bound id to ""
            // and came back 400 from the binder, which tells a client it addressed a real endpoint
            // incorrectly about a URL that addresses no endpoint at all. 404 is the truthful answer.
            wildCardMethod.If($"{span.Name}.Length <= {index.Name}").Return(Null());

            // A token that ends the route takes the rest of the path as its value, so without this
            // the route matches paths deeper than the document declares: /users/{id} answered
            // /users/42/anything/at/all. An OpenAPI template means one segment, and this is the
            // table compiled from one.
            wildCardMethod.If($"{span.Name}.Slice({index.Name}).IndexOf('/') >= 0").Return(Null());

            // The constraint decides whether the token matched at all, so a failure is no match -
            // 404 - rather than a value handed on to the binder for a 400.
            var leafConstraint = ConstraintTest(
                wildCardNode, $"{span.Name}.Slice({index.Name})", negate: true);

            if (leafConstraint != null) {
                wildCardMethod.If(leafConstraint).Return(Null());
            }

            var switchBlock = wildCardMethod.Switch(methodString);

            foreach (var leafNode in wildCardNode.LeafNodes) {
                if (RouteMethods.AddsHeadFallThrough(wildCardNode.LeafNodes, leafNode)) {
                    switchBlock.AddCase(QuoteString(RouteMethods.Head));
                }

                var caseStatement = switchBlock.AddCase(QuoteString(leafNode.Method));

                var field = routingClass.AddField(
                    leafNode.Value.InvokeHandlerType.MakeNullable(),
                    "_field" + leafNode.Value.InvokeHandlerType.Name);

                var coalesceHandler = NullCoalesceEqual(field.Instance,
                    New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));
                coalesceHandler.PrintParentheses = false;

                var pathToken = New(KnownTypes.Requests.PathToken,
                    QuoteString(wildCardNode.WildCardToken!),
                    span.Invoke("Slice", index).Invoke("ToString"));

                IOutputComponent pathTokensCollection = New(KnownTypes.Requests.PathTokenCollection,
                    wildCardNode.WildCardDepth, pathToken);

                caseStatement.Return(New(KnownTypes.Web.RequestHandlerInfo, coalesceHandler, pathTokensCollection));
            }

            switchBlock.AddDefault().Return(MethodNotAllowed(routingClass, wildCardNode.LeafNodes));
        } else {
            wildCardMethod.Return(Null());
        }
    }

    private static void ProcessLeafNodes(
        ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> routeNode,
        BaseBlockDefinition block,
        ParameterDefinition span,
        ParameterDefinition index,
        ParameterDefinition methodString,
        CancellationToken cancellationToken) {
        var ifLengthMatch = block.If("charSpan.Length == index");
        var switchStatement = ifLengthMatch.Switch(methodString);

        foreach (var leafNode in routeNode.LeafNodes) {
            cancellationToken.ThrowIfCancellationRequested();

            if (RouteMethods.AddsHeadFallThrough(routeNode.LeafNodes, leafNode)) {
                switchStatement.AddCase(QuoteString(RouteMethods.Head));
            }

            var caseStatement = switchStatement.AddCase(QuoteString(leafNode.Method));

            var field = routingClass.AddField(
                leafNode.Value.InvokeHandlerType.MakeNullable(),
                "_field" + leafNode.Value.InvokeHandlerType.Name);

            var coalesceHandler = NullCoalesceEqual(field.Instance,
                New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));
            coalesceHandler.PrintParentheses = false;

            IOutputComponent pathTokensCollection = EmptyTokens;

            if (routeNode.WildCardDepth > 0) {
                pathTokensCollection = New(KnownTypes.Requests.PathTokenCollection, routeNode.WildCardDepth);
            }

            caseStatement.Return(New(KnownTypes.Web.RequestHandlerInfo, coalesceHandler, pathTokensCollection));
        }

        switchStatement.AddDefault().Return(MethodNotAllowed(routingClass, routeNode.LeafNodes));
    }

    /// <summary>
    /// The result for a path this leaf matched under a verb it does not answer. The same rule the
    /// attribute-routed table follows, for the reason Part 0 fixed the token bound in both: two
    /// routing tables disagreeing about what a request means is worse than either being wrong.
    /// </summary>
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

    private static IReadOnlyList<IOutputComponent> CreatePathIfStatement(
        ParameterDefinition span,
        string routeNodePath,
        CancellationToken cancellationToken,
        string indexName = "index") {
        var returnList = new List<IOutputComponent>();
        returnList.Add(GreaterThanOrEquals(span.Property("Length"), indexName + " + " + routeNodePath.Length));

        int idx = 0;
        foreach (var pathChar in routeNodePath) {
            cancellationToken.ThrowIfCancellationRequested();

            var equalStatement = EqualsStatement($"{span.Name}[{indexName} + {idx}]", "'" + pathChar + "'");

            // One comparison per character unless the module asked otherwise. See
            // CaseInsensitiveRoutesAttribute: paths are case-sensitive per RFC 3986, an OpenAPI
            // document has no notion of a case-insensitive path, and the second comparison ran for
            // every letter of every literal on every request.
            if (_caseInsensitive) {
                var upperChar = char.ToUpperInvariant(pathChar);

                if (upperChar != pathChar) {
                    returnList.Add(Or(
                        equalStatement,
                        EqualsStatement($"{span.Name}[{indexName} + {idx}]", "'" + upperChar + "'")));

                    idx++;

                    continue;
                }
            }

            returnList.Add(equalStatement);

            idx++;
        }

        return returnList;
    }

    private static string GetRouteMethodName(ClassDefinition routingClass, string path, string? postfix = null) {
        if (string.IsNullOrEmpty(path)) path = "NoPath";

        var baseName = "TestPath_" +
            path.Replace("/", "Slash").Replace("-", "Dash").Replace(".", "Period").Replace("%", "Per");

        var testMethodName = baseName + postfix;
        var count = 1;
        while (routingClass.Methods.Any(m => m.Name == testMethodName)) {
            testMethodName = baseName + (++count);
        }

        return testMethodName;
    }

    private static RouteTreeNode<RequestHandlerModel> GetRoutingNodes(
        EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> endPointModels,
        CancellationToken cancellationToken) {
        var generator = new RouteTreeGenerator<RequestHandlerModel>(cancellationToken);

        return generator.GenerateTree(endPointModels.Select(
            m => new RouteTreeGenerator<RequestHandlerModel>.Entry(
                m.Name.Path,
                m.Name.Method,
                m,
                _caseInsensitive
            )).ToList());
    }
}
