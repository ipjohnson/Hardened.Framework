using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web.RouteTableGeneratorTests
{
    public class SimpleOverlappingRouteTreeTest
    {
        [Fact]
        public void GenerateRoutingTreeEmpty()
        {
            var handlerDefinitions = new List<RequestHandlerModel>();
            var applicationModel = new EntryPointSelector.Model
            {
                EntryPointType = TypeDefinition.Get("Testing", "App"),
                MethodDefinitions = Array.Empty<HardenedMethodDefinition>(),
                RootEntryPoint = true
            };

            var csharpFile = RoutingTableGenerator.GenerateCSharpRouteFile(applicationModel, handlerDefinitions, CancellationToken.None);
        }

        [Fact]
        public void GenerateRoutingTree()
        {
            var handlerDefinitions = CreateHandlerModels();
            var applicationModel = new EntryPointSelector.Model
            {
                EntryPointType = TypeDefinition.Get("Testing", "App"),
                MethodDefinitions = Array.Empty<HardenedMethodDefinition>(),
                RootEntryPoint = true
            };

            var csharpFile = RoutingTableGenerator.GenerateCSharpRouteFile(applicationModel, handlerDefinitions, CancellationToken.None);

            File.AppendAllText(@"C:\temp\generated\routing_tree.test.cs", csharpFile);
        }

        private IReadOnlyList<RequestHandlerModel> CreateHandlerModels()
        {
            var list = new List<RequestHandlerModel>();

            list.Add(new RequestHandlerModel(
                new RequestHandlerNameModel("/Home", "GET"),
                TypeDefinition.Get("Testing","Controller"),
                "SomeMethod",
                TypeDefinition.Get("Testing","Controller_SomeMethod"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(false, null, null),
                Array.Empty<FilterInformationModel>()
                ));

            list.Add(new RequestHandlerModel(
                new RequestHandlerNameModel("/Header", "GET"),
                TypeDefinition.Get("Testing", "Controller"),
                "HeaderMethod",
                TypeDefinition.Get("Testing", "Controller_HeaderMethod"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(false, null, null),
                Array.Empty<FilterInformationModel>()
            ));

            list.Add(new RequestHandlerModel(
                new RequestHandlerNameModel("/Api/person", "GET"),
                TypeDefinition.Get("Testing", "Person"),
                "GetAll",
                TypeDefinition.Get("Testing", "Person_GetAll"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(false, null, null),
                Array.Empty<FilterInformationModel>()
            ));

            list.Add(new RequestHandlerModel(
                new RequestHandlerNameModel("/Api/person/{id}", "GET"),
                TypeDefinition.Get("Testing", "Person"),
                "GetPerson",
                TypeDefinition.Get("Testing", "Person_GetPerson"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(false, null, null),
                Array.Empty<FilterInformationModel>()
            ));

            list.Add(new RequestHandlerModel(
                new RequestHandlerNameModel("/Api/person/View", "GET"),
                TypeDefinition.Get("Testing", "Person"),
                "View",
                TypeDefinition.Get("Testing", "Person_View"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(false, null, null),
                Array.Empty<FilterInformationModel>()
            ));

            return list;
        }
    }
}
