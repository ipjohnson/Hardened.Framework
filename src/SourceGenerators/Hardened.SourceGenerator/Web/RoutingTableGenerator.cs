﻿using System.Collections.Immutable;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
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
        var outputString = GenerateCSharpRouteFile(models.Left, models.Right, context.CancellationToken);

        var fileName = models.Left.EntryPointType.Name + ".Routing";

        context.AddSource(fileName, outputString);
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
        templateField.AddUsingNamespace(KnownTypes.Namespace.Hardened.Shared.Runtime.DependencyInjection);
        templateField.InitializeValue = $"DependencyRegistry<{classDefinition.Name}>.Register(RoutingTableDI)";

        var diMethod = classDefinition.AddMethod("RoutingTableDI");

        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var environment = diMethod.AddParameter(KnownTypes.Application.IHardenedEnvironment, "environment");
        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
        var entryPoint = diMethod.AddParameter(applicationModel.EntryPointType, "entryPoint");

        diMethod.AddIndentedStatement(Invoke(
            KnownTypes.DI.Registry.RequestRuntimeDI, "Register", environment, serviceCollection));

        diMethod.AddIndentedStatement(Invoke(
            KnownTypes.DI.Registry.WebRuntimeDI, "Register", environment, serviceCollection));

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] { KnownTypes.Web.IWebExecutionRequestHandlerProvider, routingTableType }));

        var distinctControllers =
            webEndPointModels.Select(model => model.ControllerType).Distinct();

        foreach (var controllerType in distinctControllers) {
            cancellationToken.ThrowIfCancellationRequested();

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
                new[] { controllerType }));
        }

        var registerFiltersMethod =
            applicationModel.MethodDefinitions.FirstOrDefault(m => m.Name == "RegisterFilters");

        if (registerFiltersMethod != null) {
            diMethod.AddIndentedStatement(
                serviceCollection.InvokeGeneric(
                    "AddSingleton",
                    new[] { KnownTypes.Application.IStartupService },
                    New(KnownTypes.Web.FilterRegistryStartupService, "entryPoint.RegisterFilters")
                ));
        }
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

        var whileBlock =
            wildCardMethod.While(LessThan(currentIndex, span.Property("Length")));

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

        var newPathToken = New(KnownTypes.Requests.PathToken,
            QuoteString(wildCardNode.WildCardToken!),
            span.Invoke(
                "Slice",
                index,
                Subtract(currentIndex, index)).Invoke("ToString")
        );

        matchIfHandlerBlock.AddIndentedStatement(
            handlerInfo.Property("PathTokens").Invoke(
                "Set",
                wildCardNode.WildCardDepth - 1,
                newPathToken
            ));

        matchIfHandlerBlock.Return(handlerInfo);

        whileBlock.AddIndentedStatement(Increment(currentIndex));
    }

    private static void GenerateWildCardLeafNode(ClassDefinition routingClass,
        RouteTreeNode<RequestHandlerModel> wildCardNode,
        MethodDefinition wildCardMethod, ParameterDefinition methodString, ParameterDefinition span,
        ParameterDefinition index) {
        if (wildCardNode.LeafNodes.Count > 0) {
            var switchBlock = wildCardMethod.Switch(methodString);

            foreach (var leafNode in wildCardNode.LeafNodes) {
                var caseStatement = switchBlock.AddCase(QuoteString(leafNode.Method));

                var field =
                    routingClass.AddField(leafNode.Value.InvokeHandlerType.MakeNullable(),
                        "_field" + leafNode.Value.InvokeHandlerType.Name);

                var coalesceHandler = NullCoalesceEqual(field.Instance,
                    New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));

                coalesceHandler.PrintParentheses = false;

                var pathToken = New(KnownTypes.Requests.PathToken,
                    QuoteString(wildCardNode.WildCardToken!),
                    span.Invoke("Slice", index).Invoke("ToString")
                );

                IOutputComponent pathTokensCollection =
                    New(KnownTypes.Requests.PathTokenCollection,
                        wildCardNode.WildCardDepth,
                        pathToken
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

            var caseStatement = switchStatement.AddCase(QuoteString(leafNode.Method));

            var field =
                routingClass.AddField(leafNode.Value.InvokeHandlerType.MakeNullable(),
                    "_field" + leafNode.Value.InvokeHandlerType.Name);

            var coalesceHandler = NullCoalesceEqual(field.Instance,
                New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));

            coalesceHandler.PrintParentheses = false;

            IOutputComponent pathTokensCollection = EmptyTokens;

            if (routeNode.WildCardDepth > 0) {
                pathTokensCollection = New(KnownTypes.Requests.PathTokenCollection, routeNode.WildCardDepth);
            }

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
}