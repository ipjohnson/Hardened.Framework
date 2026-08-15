using System.Collections.Immutable;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Links;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.OpenApiDocument;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;
using System.CodeDom.Compiler;

namespace Hardened.SourceGenerator.Web;

public static class RoutingTableGenerator {
    private static readonly IOutputComponent EmptyTokens =
        Property(KnownTypes.Requests.PathTokenCollection, "Empty");

    public static void GenerateRoute(SourceProductionContext context,
        (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models) {
        // Handlers that were not generated must not be routed to. Routing to one would emit a
        // table referencing a handler class that does not exist - uncompilable output, which is
        // worse than the missing route. Skipped silently: WebExecutionHandlerCodeGenerator has
        // already reported each one, and it runs per handler rather than per table.
        var routable = models.Right
            .Where(handler => handler.UnresolvedParameter() == null)
            .ToList();

        var outputString = GenerateCSharpRouteFile(models.Left, routable, context.CancellationToken);

        var fileName = models.Left.EntryPointType.Name + ".Routing";

        context.AddSource(fileName, outputString);

        context.AddSource(
            models.Left.EntryPointType.Name + ".OpenApiDocument",
            OpenApiDocumentSource.Write(models.Left, routable, GetBasePath(models.Left)));

        // From the same models the table came from, and unconditionally: links have no third-party
        // dependency, and the common case is an API with no views that still wants them for
        // Location headers - which are exactly the strings that rot.
        LinkGenerator.Generate(context, models.Left, routable, GetBasePath(models.Left));
    }
    
    public static string GenerateCSharpRouteFile(EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> handlers, CancellationToken cancellationToken) {
        var applicationFile = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        CreateRoutingTable(appModel, handlers, applicationFile, cancellationToken);

        var outputContext = new OutputContext();

        applicationFile.WriteOutput(outputContext);

        return outputContext.Output();
    }

    private static void CreateRoutingTable(EntryPointSelector.Model appModel,
        IReadOnlyList<RequestHandlerModel> endPointModels,
        CSharpFileDefinition applicationFile, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var appClass = applicationFile.AddClass(appModel.EntryPointType.Name);

        appClass.Modifiers |= ComponentModifier.Partial;

        var routingClass = appClass.AddClass("RoutingTable");

        CreateConstructor(routingClass);

        routingClass.Modifiers |= ComponentModifier.Private;

        routingClass.AddBaseType(KnownTypes.Web.IWebExecutionRequestHandlerProvider);

        ImplementHandlerMethod(appModel, routingClass, endPointModels, cancellationToken);

        var routingType = TypeDefinition.Get(appModel.EntryPointType.Namespace,
            appModel.EntryPointType.Name + ".RoutingTable");

        GenerateDependencyInjection(appClass, routingType, appModel, endPointModels, cancellationToken);
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
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var templateField = classDefinition.AddField(typeof(int), "_routingTableDependencies");

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace(KnownTypes.Namespace.DependencyModules.Runtime.Helpers);
        templateField.InitializeValue = new CodeOutputComponent($"DependencyRegistry<{classDefinition.Name}>.Add(RoutingTableDI)");
        templateField.AddAttribute(TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"), "nameof(RoutingTableDI)");

        var diMethod = classDefinition.AddMethod("RoutingTableDI");

        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] { KnownTypes.Web.IWebExecutionRequestHandlerProvider, routingTableType }));

        var distinctControllers =
            webEndPointModels.Select(model => model.ControllerType).Distinct();

        foreach (var controllerType in distinctControllers) {
            cancellationToken.ThrowIfCancellationRequested();

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
                new[] { controllerType }));
        }

        RegisterLinks(diMethod, serviceCollection, applicationModel, webEndPointModels);

        Validation.ParameterValidatorRegistration.Write(
            diMethod, serviceCollection, webEndPointModels, cancellationToken);
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

    private static void ImplementHandlerMethod(EntryPointSelector.Model appModel, ClassDefinition routingClass,
        IReadOnlyList<RequestHandlerModel> endPointModels, CancellationToken cancellationToken) {
        var handlerMethod = routingClass.AddMethod("GetExecutionRequestHandler");

        handlerMethod.SetReturnType(KnownTypes.Web.RequestHandlerInfo.MakeNullable());

        var context = handlerMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");

        handlerMethod.Assign(context.Property("Request").Property("Path").Invoke("AsSpan")).ToVar("pathSpan");

        WriteRoutingTable(appModel, routingClass, handlerMethod, endPointModels,
            context.Property("Request").Property("Method"), cancellationToken);
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

            var lowerChar = char.ToLowerInvariant(childNode.Path.First());
            var upperChar = char.ToUpperInvariant(lowerChar);

            if (upperChar != lowerChar) {
                switchStatement.AddCase($"'{upperChar}'");
            }

            var caseStatement = switchStatement.AddCase($"'{lowerChar}'");

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

        // The value is positional. Its name belongs to whichever route matched, which is
        // only known further down, so the collection was created with that route's names.
        matchIfHandlerBlock.AddIndentedStatement(
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

            if (!catchAll) {
                wildCardMethod.If($"{span.Name}.Slice({index.Name}).IndexOf('/') >= 0").Return(Null());
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
                    New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));

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

            switchBlock.AddDefault().Return(Null());
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
                        New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"),
                        EmptyTokens));

                cachedInfo.PrintParentheses = false;

                caseStatement.Return(cachedInfo);

                continue;
            }

            var field =
                routingClass.AddField(leafNode.Value.InvokeHandlerType.MakeNullable(),
                    "_field" + leafNode.Value.InvokeHandlerType.Name);

            var coalesceHandler = NullCoalesceEqual(field.Instance,
                New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));

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

        switchStatement.AddDefault().Return(Null());
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

            var upperChar = char.ToUpper(pathChar);

            var lowerEqualStatement = EqualsStatement($"{span.Name}[{indexName} + {index}]", "'" + pathChar + "'");

            if (upperChar != pathChar) {
                var upperEqualStatement = EqualsStatement($"{span.Name}[{indexName} + {index}]", "'" + upperChar + "'");

                returnList.Add(Or(lowerEqualStatement, upperEqualStatement));
            }
            else {
                returnList.Add(lowerEqualStatement);
            }

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
                basePath + m.Name.Path,
                m.Name.Method,
                m
            )).ToList());
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