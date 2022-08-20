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
            (ApplicationSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models)
        {
            var applicationFile = new CSharpFileDefinition(models.Left.ApplicationType.Namespace);

            CreateRoutingTable(models.Left, models.Right, applicationFile);

            var outputContext = new OutputContext();

            applicationFile.WriteOutput(outputContext);

            var fileName = models.Left.ApplicationType.Name + ".Routing";

            File.AppendAllText(@"c:\temp\" + fileName + ".cs", outputContext.Output());

            File.AppendAllLines(@"c:\temp\paths.txt", models.Right.Select(m => m.Name.Path));

            context.AddSource(fileName, outputContext.Output());
        }

        private static void CreateRoutingTable(
            ApplicationSelector.Model appModel, ImmutableArray<RequestHandlerModel> endPointModels, CSharpFileDefinition applicationFile)
        {
            var appClass = applicationFile.AddClass(appModel.ApplicationType.Name);
            
            appClass.Modifiers |= ComponentModifier.Partial;

            var routingClass = appClass.AddClass("RoutingTable");

            CreateConstructor(routingClass);

            routingClass.Modifiers |= ComponentModifier.Private;

            routingClass.AddBaseType(KnownTypes.Web.IWebExecutionRequestHandlerProvider);

            ImplementHandlerMethod(routingClass, endPointModels);
            var routingType = TypeDefinition.Get(appModel.ApplicationType.Namespace,
                appModel.ApplicationType.Name + ".RoutingTable");

            GenerateDependencyInjection(appClass, routingType, appModel, endPointModels);
        }

        private static void CreateConstructor(ClassDefinition appClass)
        {
            var field = appClass.AddField(typeof(IServiceProvider), "_rootServiceProvider");

            var constructor = appClass.AddConstructor();

            var parameter = constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");

            constructor.Assign(parameter).To(field.Instance);
        }

        private static void GenerateDependencyInjection(ClassDefinition classDefinition, ITypeDefinition routingTableType, ApplicationSelector.Model applicationModel, ImmutableArray<RequestHandlerModel> webEndPointModels)
        {
            var templateField = classDefinition.AddField(typeof(int), "_routingTableDependencies");

            templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
            templateField.AddUsingNamespace(KnownTypes.Namespace.HardenedSharedRuntimeDependencyInjection);
            templateField.InitializeValue = $"DependencyRegistry<{classDefinition.Name}>.Register(RoutingTableDI)";

            var diMethod = classDefinition.AddMethod("RoutingTableDI");

            diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

            var environment = diMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
            var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
            var entryPoint = diMethod.AddParameter(applicationModel.ApplicationType, "entryPoint");
            
            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
                new[] { KnownTypes.Web.IWebExecutionRequestHandlerProvider, routingTableType }));

            var distinctControllers =
                webEndPointModels.Select(model => model.ControllerType).Distinct();

            foreach (var controllerType in distinctControllers)
            {
                diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddTransient",
                    new[] { controllerType }));
            }

            var registerFiltersMethod = applicationModel.MethodDefinitions.FirstOrDefault(m => m.Name == "RegisterFilters");

            if (registerFiltersMethod != null)
            {
                diMethod.AddIndentedStatement(
                    serviceCollection.InvokeGeneric(
                        "AddSingleton", 
                        new []{ KnownTypes.Application.IStartupService}, 
                        New(KnownTypes.Web.FilterRegistryStartupService, "entryPoint.RegisterFilters")
                ));
            }
        }

        private static void ImplementHandlerMethod(ClassDefinition routingClass, ImmutableArray<RequestHandlerModel> endPointModels)
        {
            var handlerMethod = routingClass.AddMethod("GetExecutionRequestHandler");

            handlerMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());

            var context = handlerMethod.AddParameter(KnownTypes.Requests.IExecutionContext, "context");

            handlerMethod.Assign(context.Property("Request").Property("Path").Invoke("AsSpan")).ToVar("pathSpan");

            WriteRoutingTable(routingClass, handlerMethod, endPointModels, context.Property("Request").Property("Method"));
        }

        private static void WriteRoutingTable(ClassDefinition routingClass, MethodDefinition handlerMethod,
            ImmutableArray<RequestHandlerModel> endPointModels, InstanceDefinition methodString)
        {
            var routeNode = GetRoutingNodes(endPointModels);

            var routeTestMethod = WriteRouteNode(routingClass, routeNode, true);
            
            handlerMethod.Return(Invoke(routeTestMethod, "pathSpan", 0, methodString));
        }

        private static string WriteRouteNode(ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> routeNode, bool pathTest)
        {
            var routeMethodName = GetRouteMethodName(routingClass, routeNode);

            var testMethod = routingClass.AddMethod(routeMethodName);
            testMethod.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());

            var span = testMethod.AddParameter(typeof(ReadOnlySpan<char>), "charSpan");
            var index = testMethod.AddParameter(typeof(int), "index");
            var methodString = testMethod.AddParameter(typeof(string), "methodString");

            var handler = 
                testMethod.Assign(Null()).ToLocal(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable(), "handler");
            
            BaseBlockDefinition block = testMethod;

            if (pathTest)
            {
                var pathIfStatement = CreatePathIfStatement(span, routeNode.Path);

                block =
                    testMethod.If(And(pathIfStatement));
            }
            
            if (routeNode.ChildNodes.Count > 0)
            {
                if (routeNode.ChildNodes.All(m => m.Path.Length == 1))
                {
                    ImplementSwitchRouteNodes(routingClass, testMethod, handler, span, index, methodString, block, routeNode,
                        pathTest);
                }
                else
                {
                    WriteIfRouteNodes(routingClass, testMethod, handler, span, index, methodString, block, routeNode, pathTest);
                }
            }

            if (routeNode.LeafNodes.Count > 0)
            {
                var ifBlock = 
                    block.If(EqualsStatement(span.Property("Length"), "index + " + routeNode.Path.Length));

                IfElseLogicBlockDefinition? leafIfBlock = null;

                foreach (var leafNode in routeNode.LeafNodes)
                {
                    if (leafIfBlock == null)
                    {
                        leafIfBlock = ifBlock.If(
                            EqualsStatement(methodString, QuoteString(leafNode.Method)));

                        var field = 
                            routingClass.AddField(leafNode.Value.InvokeHandlerType, "_field" + leafNode.Value.InvokeHandlerType.Name);

                        var coalesceHandler = NullCoalesceEqual(field.Instance,
                            New(leafNode.Value.InvokeHandlerType, "_rootServiceProvider"));

                        leafIfBlock.Assign(coalesceHandler).To(handler);
                    }
                }
            }

            testMethod.Return(handler);
            
            return routeMethodName;
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

        private static void WriteIfRouteNodes(ClassDefinition routingClass,
            MethodDefinition testMethod,
            InstanceDefinition handler,
            ParameterDefinition charSpan,
            ParameterDefinition index,
            ParameterDefinition methodString,
            BaseBlockDefinition block,
            RouteTreeNode<RequestHandlerModel> routeNode,
            bool pathTest)
        {
            if (routeNode.ChildNodes.Count > 0)
            {
                var childNode = routeNode.ChildNodes[0];

                var routeNodeMethod = WriteRouteNode(routingClass, childNode, true);

                block.Assign(Invoke(routeNodeMethod, charSpan, index, "methodString")).To(handler);
            }
        }

        private static void ImplementSwitchRouteNodes(ClassDefinition routingClass,
            MethodDefinition testMethod,
            InstanceDefinition handler,
            ParameterDefinition charSpan,
            ParameterDefinition index,
            ParameterDefinition methodString,
            BaseBlockDefinition baseBlockDefinition,
            RouteTreeNode<RequestHandlerModel> routeNode,
            bool pathTest)
        {
            var indexLength = routeNode.Path.Length + 1;

            var ifBlock = 
                baseBlockDefinition.If(
                GreaterThanOrEquals(charSpan.Property("Length"), "index + " + indexLength));

            var switchBlock = ifBlock.Switch($"charSpan[index + {routeNode.Path.Length}]");

            foreach (var childNode in routeNode.ChildNodes)
            {
                var character = childNode.Path.First();
                var characterUpper = char.ToUpper(character);

                if (characterUpper != character)
                {
                    switchBlock.AddCase("'" + characterUpper + "'");
                }

                var childCase = switchBlock.AddCase("'" + character + "'");

                var routeNodeMethod = WriteRouteNode(routingClass, childNode, false);

                childCase.Assign(Invoke(routeNodeMethod, charSpan, "index + " + indexLength, "methodString")).To(handler);

                childCase.Break();
            }
        }

        private static string GetRouteMethodName(ClassDefinition routingClass, RouteTreeNode<RequestHandlerModel> routeNode)
        {
            var baseName = "TestPath_" +
                routeNode.Path.Replace("/", "Slash").Replace("-","Dash").Replace(".","Period").Replace("%", "Per");

            var testMethodName = baseName;
            var count = 1;
            while (routingClass.Methods.Any(m => m.Name == testMethodName))
            {
                testMethodName = baseName + (++count);
            }
            return testMethodName;
        }

        private static RouteTreeNode<RequestHandlerModel> GetRoutingNodes(ImmutableArray<RequestHandlerModel> endPointModels)
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
