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
            RootEntryPoint = true,
        };

        var csharpFile = RoutingTableGenerator.GenerateCSharpRouteFile(
            applicationModel,
            handlerDefinitions,
            CancellationToken.None
        );
    }

    /// <summary>
    /// A token position where one route continues with a literal and another with a second token.
    /// </summary>
    /// <remarks>
    /// These two share a wildcard node, and that node has both a child and a wildcard child.
    /// WriteWildCardMatchMethod used to emit the scan once per kind, which declared the same local
    /// twice and added the same handler field twice, and CSharpAuthor threw on the second. A
    /// generator that throws produces no source at all, so the whole project lost its generated
    /// code over one pair of routes.
    ///
    /// The existing case above does not reach it: /company/... and /companies/... diverge on the
    /// literal before the token, so neither wildcard node has both.
    /// </remarks>
    [Fact]
    public void LiteralAndTokenContinuationsShareAWildCardNode()
    {
        var applicationModel = new EntryPointSelector.Model
        {
            EntryPointType = TypeDefinition.Get("Testing", "App"),
            MethodDefinitions = Array.Empty<HardenedMethodDefinition>(),
            RootEntryPoint = true,
        };

        var handlerDefinitions = new List<RequestHandlerModel>
        {
            new(
                new RequestHandlerNameModel("/probe/{id}/events", "GET"),
                TypeDefinition.Get("Testing", "Controller"),
                "Events",
                TypeDefinition.Get("Testing", "Controller_Events"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(),
                Array.Empty<AttributeModel>()
            ),
            new(
                new RequestHandlerNameModel("/probe/{id}/{sub}", "GET"),
                TypeDefinition.Get("Testing", "Controller"),
                "Sub",
                TypeDefinition.Get("Testing", "Controller_Sub"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(),
                Array.Empty<AttributeModel>()
            ),
        };

        var csharpFile = RoutingTableGenerator.GenerateCSharpRouteFile(
            applicationModel,
            handlerDefinitions,
            CancellationToken.None
        );

        Assert.Contains("Controller_Events", csharpFile);
        Assert.Contains("Controller_Sub", csharpFile);

        // One declaration of the scan's locals, not two. The duplicate is what threw, so this
        // fails as a crash rather than an assertion if it comes back; the count keeps it honest
        // if CSharpAuthor ever stops minding.
        Assert.Equal(1, Occurrences(csharpFile, "var currentIndex = "));
    }

    private static int Occurrences(string source, string value)
    {
        var count = 0;
        var index = source.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private IReadOnlyList<RequestHandlerModel> CreateHandlerModels()
    {
        var list = new List<RequestHandlerModel>
        {
            new(
                new RequestHandlerNameModel("/company/{company}/Subscription/{id}", "GET"),
                TypeDefinition.Get("Testing", "Controller"),
                "SomeMethod",
                TypeDefinition.Get("Testing", "Controller_SomeMethod"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(),
                Array.Empty<AttributeModel>()
            ),
            new(
                new RequestHandlerNameModel("/companies/{company}/{id}", "GET"),
                TypeDefinition.Get("Testing", "Controller"),
                "HeaderMethod",
                TypeDefinition.Get("Testing", "Controller_HeaderMethod"),
                Array.Empty<RequestParameterInformation>(),
                new ResponseInformationModel(),
                Array.Empty<AttributeModel>()
            ),
        };

        return list;
    }
}
