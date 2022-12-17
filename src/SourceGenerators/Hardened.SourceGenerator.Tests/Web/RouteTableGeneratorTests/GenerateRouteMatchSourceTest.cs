using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web.RouteTableGeneratorTests;

public class GenerateRouteMatchSourceTest
{
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

    }

    private IReadOnlyList<RequestHandlerModel> CreateHandlerModels()
    {
        var list = new List<RequestHandlerModel>
        {
            new(
                new RequestHandlerNameModel("/company/{company}/Subscription/{id}", "GET"),
                TypeDefinition.Get("Testing","Controller"),
                "SomeMethod",
                TypeDefinition.Get("Testing","Controller_SomeMethod"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(),
                Array.Empty<FilterInformationModel>()
            ),
            new(
                new RequestHandlerNameModel("/companies/{company}/{id}", "GET"),
                TypeDefinition.Get("Testing", "Controller"),
                "HeaderMethod",
                TypeDefinition.Get("Testing", "Controller_HeaderMethod"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(),
                Array.Empty<FilterInformationModel>()
            ),
        };

        return list;
    }
}