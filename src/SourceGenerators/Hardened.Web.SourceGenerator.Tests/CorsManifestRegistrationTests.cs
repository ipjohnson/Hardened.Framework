using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// Whether the routing table registers <c>CorsManifest</c>, whose presence moves CORS from the
/// whole application to the routes that declare it.
/// </summary>
public class CorsManifestRegistrationTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute), // Hardened.Web.Runtime, which also declares [Cors]
        typeof(FromBodyAttribute), // Hardened.Requests.Abstract
        typeof(EnableAttribute<>), // Hardened.Shared.Runtime
    ];

    private static readonly Regex Registration = new(
        @"AddSingleton<(global::)?(Hardened\.Web\.Runtime\.Cors\.)?CorsManifest>\(\)"
    );

    private static string Routing(string entryPoint, string controller, string method)
    {
        var source = $$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Cors;

            namespace TestApp;

            public sealed class Partners { }

            [HardenedModule]
            {{entryPoint}}
            public partial class Application { }

            {{controller}}
            public class OrdersController {
                [Get("/orders")]
                {{method}}
                public string List() => "";

                [Get("/orders/{id}")]
                public string Read(string id) => id;
            }
            """;

        return GeneratorTestHarness
            .Run(
                new Dictionary<string, string> { ["Test.cs"] = source },
                new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
                Anchors
            )
            .AssertNoErrors()
            .SourceContaining("Routing");
    }

    /// <summary>
    /// An application that declares nothing keeps CORS on every request, so its table registers
    /// nothing new.
    /// </summary>
    [Fact]
    public void AnApplicationDeclaringNoCorsRegistersNoManifest()
    {
        Assert.DoesNotMatch(Registration, Routing("", "", ""));
    }

    [Theory]
    [InlineData("[Cors]", "", "")]
    [InlineData("", "[Cors]", "")]
    [InlineData("", "", "[Cors]")]
    [InlineData("", "", "[Cors<Partners>]")]
    public void ADeclarationOnTheModuleAClassOrAMethodRegistersTheManifest(
        string entryPoint,
        string controller,
        string method
    )
    {
        Assert.Matches(Registration, Routing(entryPoint, controller, method));
    }
}
