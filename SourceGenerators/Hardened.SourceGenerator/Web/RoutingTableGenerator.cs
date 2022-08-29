using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Generator;
using Hardened.SourceGenerator.Web.Routing;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Web
{
    public static class RoutingTableGenerator
    {
        public static void GenerateRoute(SourceProductionContext context,
            (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models)
        {
            var outputString = GenerateCSharpRouteFile(models.Left, models.Right);

            var fileName = models.Left.EntryPointType.Name + ".Routing";

            File.AppendAllText(@"c:\temp\" + fileName + ".cs", outputString);

            File.AppendAllLines(@"c:\temp\paths.txt", models.Right.Select(m => m.Name.Path));

            context.AddSource(fileName, outputString);
        }

        public static string GenerateCSharpRouteFile(EntryPointSelector.Model appModel,
            IReadOnlyList<RequestHandlerModel> handlers)
        {
            var applicationFile = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

            CreateRoutingTable(appModel, handlers, applicationFile);

            var outputContext = new OutputContext();

            applicationFile.WriteOutput(outputContext);

            return outputContext.Output();
        }

        private static void CreateRoutingTable(
            EntryPointSelector.Model appModel, IReadOnlyList<RequestHandlerModel> endPointModels,
            CSharpFileDefinition applicationFile)
        {
            var appClass = applicationFile.AddClass(appModel.EntryPointType.Name);

            appClass.Modifiers |= ComponentModifier.Partial;

            var routingClass = appClass.AddClass("RoutingTable");

            CreateConstructor(routingClass);

            routingClass.Modifiers |= ComponentModifier.Private;

            routingClass.AddBaseType(KnownTypes.Web.IWebExecutionRequestHandlerProvider);

            ImplementHandlerMethod(routingClass, endPointModels);
            var routingType = TypeDefinition.Get(appModel.EntryPointType.Namespace,
                appModel.EntryPointType.Name + ".RoutingTable");

            GenerateDependencyInjection(appClass, routingType, appModel, endPointModels);
        }

        private static void CreateConstructor(ClassDefinition appClass)
        {
            var field = appClass.AddField(typeof(IServiceProvider), "_rootServiceProvider");

            var constructor = appClass.AddConstructor();

            var parameter = constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");

            constructor.Assign(parameter).To(field.Instance);
        }

        private static void GenerateDependencyInjection(
            ClassDefinition classDefinition, ITypeDefinition routingTableType,
            EntryPointSelector.Model applicationModel, IReadOnlyList<RequestHandlerModel> webEndPointModels)
        {
            var templateField = classDefinition.AddField(typeof(int), "_routingTableDependencies");

            templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
            templateField.AddUsingNamespace(KnownTypes.Namespace.HardenedSharedRuntimeDependencyInjection);
            templateField.InitializeValue = $"DependencyRegistry<{classDefinition.Name}>.Register(RoutingTableDI)";

            var diMethod = classDefinition.AddMethod("RoutingTableDI");

            diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

            var environment = diMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
            var entryPoint = diMethod.AddParameter(applicationModel.EntryPointType, "entryPoint");

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                new[] { KnownTypes.Web.IWebExecutionRequestHandlerProvider, routingTableType }));

            var distinctControllers =
                webEndPointModels.Select(model => model.ControllerType).Distinct();

            foreach (var controllerType in distinctControllers)
            {
                diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
                    new[] { controllerType }));
            }

            var registerFiltersMethod =
                applicationModel.MethodDefinitions.FirstOrDefault(m => m.Name == "RegisterFilters");

            if (registerFiltersMethod != null)
            {
                diMethod.AddIndentedStatement(
                    serviceCollection.InvokeGeneric(
                        "AddSingleton",
                        new[] { KnownTypes.Application.IStartupService },
                        New(KnownTypes.Web.FilterRegistryStartupService, "entryPoint.RegisterFilters")
                    ));
            }
        }

        private static void ImplementHandlerMethod(ClassDefinition routingClass,
            IReadOnlyList<RequestHandlerModel> endPointModels)
        {
            var handlerMethod = routingClass.AddMethod("GetExecutionRequestHandler");

            handlerMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());

            var context = handlerMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");

            handlerMethod.Assign(context.Property("Request").Property("Path").Invoke("AsSpan")).ToVar("pathSpan");

            WriteRoutingTable(routingClass, handlerMethod, endPointModels,
                context.Property("Request").Property("Method"));
        }

        private static void WriteRoutingTable(
            ClassDefinition routingClass,
            MethodDefinition handlerMethod,
            IReadOnlyList<RequestHandlerModel> endPointModels,
            InstanceDefinition methodString)
        {
            var routeNode = GetRoutingNodes(endPointModels);

            var routeTestMethod = WriteRouteNode(routingClass, routeNode, 0);

            handlerMethod.Return(Invoke(routeTestMethod, "pathSpan", 0, methodString));
        }
        private static string WriteRouteNode(ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> routeNode, int pathIndex)
        {
            var path = routeNode.Path;

            if (pathIndex > 0)
            {
                if (path.Length < 2)
                {
                    path = "";
                }
                else
                {
                    path = path.Substring(1);
                }
            }

            var routeMethodName = GetRouteMethodName(routingClass, path);
            
            var testMethod = routingClass.AddMethod(routeMethodName);
            testMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());

            var span = testMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
            var index = testMethod.AddParameter(typeof(int), "index");
            var methodString = testMethod.AddParameter(typeof(string), "methodString");

            var handler =
                testMethod.Assign(Null()).ToLocal(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable(), "handler");

            BaseBlockDefinition block = testMethod;

            if (!string.IsNullOrEmpty(path))
            {
                var pathIfStatement = CreatePathIfStatement(span, routeNode.Path);

                block = testMethod.If(And(pathIfStatement));

                block.AddIndentedStatement("index += " + path.Length);
            }

            if (routeNode.LeafNodes.Count > 0)
            {
                ProcessLeafNodes(routingClass, routeNode, block, span, index, methodString);
            }

            if (routeNode.ChildNodes.Count > 0)
            {
                ProcessChildNodes(routingClass, routeNode, block, span, index, methodString, handler);
            }

            if (routeNode.WildCardNodes.Count > 0)
            {
                ProcessWildCardNodes(routingClass, routeNode, block, span, index, methodString, handler);
            }

            testMethod.Return(handler);

            return routeMethodName;
        }

        private static void ProcessChildNodes(ClassDefinition routingClass,
            RouteTreeNode<RequestHandlerModel> routeNode,
            BaseBlockDefinition block,
            ParameterDefinition span,
            ParameterDefinition index,
            ParameterDefinition methodString, 
            InstanceDefinition handler)
        {
            var childMethod = "";
            
            if (routeNode.ChildNodes.Count == 1)
            {
                childMethod = WriteRouteNode(routingClass, routeNode.ChildNodes.First(), 0);
            }
            else
            {
                childMethod = WriteSwitchChildNode(routingClass, routeNode);
            }

            block.Assign(Invoke(childMethod, span, index, methodString)).To(handler);
        }

        private static string WriteSwitchChildNode(ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> routeNode)
        {
            var switchMethodName = GetRouteMethodName(routingClass, routeNode.Path,"CaseStatement");

            var switchMethod = routingClass.AddMethod(switchMethodName);
            switchMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());
            var span = switchMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
            var index = switchMethod.AddParameter(typeof(int), "index");
            var methodString = switchMethod.AddParameter(typeof(string), "methodString");

            var ifStatement = switchMethod.If("charSpan.Length > index");

            var switchStatement = ifStatement.Switch("charSpan[index]");

            foreach (var childNode in routeNode.ChildNodes)
            {
                var lowerChar = char.ToLowerInvariant(childNode.Path.First());
                var upperChar = char.ToUpperInvariant(lowerChar);

                if (upperChar != lowerChar)
                {
                    switchStatement.AddCase($"'{upperChar}'");
                }

                var caseStatement = switchStatement.AddCase($"'{lowerChar}'");

                var newMethodName = WriteRouteNode(routingClass, childNode, 1);

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
            ParameterDefinition index,
            ParameterDefinition methodString,
            InstanceDefinition handler)
        {
            var ifBlock = block.If("handler == null");

            var wildCardMethod = WriteWildCardMethod(routingClass, routeNode);

            var invoke = Invoke(wildCardMethod, span, index, methodString);

            ifBlock.Assign(invoke).To(handler);
        }

        private static string WriteWildCardMethod(ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> routeNode)
        {
            var methodName = GetRouteMethodName(routingClass, routeNode.Path, "WildCard");

            var wildCardMethod = routingClass.AddMethod(methodName);

            wildCardMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());
            var span = wildCardMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
            var index = wildCardMethod.AddParameter(typeof(int), "index");
            var methodString = wildCardMethod.AddParameter(typeof(string), "methodString");


            var handler =
                wildCardMethod.Assign(Null()).ToLocal(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable(), "handler");

            for (var i  = 0; i < routeNode.WildCardNodes.Count; i ++)
            {
                var wildCardNode = routeNode.WildCardNodes[i];
                BaseBlockDefinition currentBlock = wildCardMethod;

                if (i > 0)
                {
                    currentBlock = wildCardMethod.If("handler == null");
                }

                var matchWildCardMethod = WriteWildCardMatchMethod(routingClass, wildCardNode);

                currentBlock.Assign(Invoke(matchWildCardMethod, span, index, methodString)).To(handler);
            }

            wildCardMethod.Return(handler);

            return methodName;
        }

        private static string WriteWildCardMatchMethod(ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> wildCardNode)
        {
            var methodName = GetRouteMethodName(routingClass, wildCardNode.Path, "WildCardMatch");

            var wildCardMethod = routingClass.AddMethod(methodName);

            wildCardMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());
            var span = wildCardMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
            var index = wildCardMethod.AddParameter(typeof(int), "index");
            var methodString = wildCardMethod.AddParameter(typeof(string), "methodString");

            wildCardMethod.Return(Null());

            return methodName;
        }

        private static void ProcessLeafNodes(
            ClassDefinition routingClass, 
            RouteTreeNode<RequestHandlerModel> routeNode,
            BaseBlockDefinition block, 
            ParameterDefinition span, 
            ParameterDefinition index, 
            ParameterDefinition methodString)
        {
            var ifLengthMatch = block.If("charSpan.Length == index");

            var switchStatement = ifLengthMatch.Switch(methodString);

            foreach (var leafNode in routeNode.LeafNodes)
            {
                var caseStatement = switchStatement.AddCase(QuoteString(leafNode.Method));

                var field =
                    routingClass.AddField(leafNode.Value.InvokeHandlerType, "_field" + leafNode.Value.InvokeHandlerType.Name);

                var coalesceHandler = NullCoalesceEqual(field.Instance,
                    New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));

                caseStatement.Return(coalesceHandler);
            }

            switchStatement.AddDefault().Return(Null());
        }

        private static IReadOnlyList<IOutputComponent> CreatePathIfStatement(ParameterDefinition span,
            string routeNodePath)
        {
            var returnList = new List<IOutputComponent>();

            returnList.Add(GreaterThanOrEquals(span.Property("Length"), "index + " + routeNodePath.Length));

            int index = 0;
            foreach (var pathChar in routeNodePath)
            {
                var upperChar = char.ToUpper(pathChar);

                var lowerEqualStatement = EqualsStatement($"{span.Name}[index + {index}]", "'" + pathChar + "'");

                if (upperChar != pathChar)
                {
                    var upperEqualStatement = EqualsStatement($"{span.Name}[index + {index}]", "'" + upperChar + "'");

                    returnList.Add(Or(lowerEqualStatement, upperEqualStatement));
                }
                else
                {
                    returnList.Add(lowerEqualStatement);
                }

                index++;
            }

            return returnList;
        }
        private static string GetRouteMethodName(ClassDefinition routingClass,
            string path, string? postfix = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                path = "NoPath";
            }

            var baseName = "TestPath_" +
                path.Replace("/", "Slash").Replace("-", "Dash").Replace(".", "Period").Replace("%", "Per");

            var testMethodName = baseName + postfix;
            var count = 1;
            while (routingClass.Methods.Any(m => m.Name == testMethodName))
            {
                testMethodName = baseName + (++count);
            }
            return testMethodName;
        }

        private static RouteTreeNode<RequestHandlerModel> GetRoutingNodes(IReadOnlyList<RequestHandlerModel> endPointModels)
        {
            var generator = new RouteTreeGenerator<RequestHandlerModel>();

            return generator.GenerateTree(endPointModels.Select(
                m => new RouteTreeGenerator<RequestHandlerModel>.Entry(
                    m.Name.Path,
                    m.Name.Method,
                    m
                )).ToList());
        }
    }
}
