using Hardened.Library.SourceGenerator;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// A handler method parameter that names the interface of a <c>[ConfigurationModel]</c> class in
/// the same project.
/// </summary>
/// <remarks>
/// Issue #539. The interface is written by the library generator, which the web generator cannot
/// see, so the web generator emitted it as <c>global::ITodoListOptions</c> and the build failed with
/// <c>CS0400</c>.
/// </remarks>
public class ConfigurationModelParameterTests
{
    private static readonly Type[] Anchors =
    [
        typeof(GetAttribute), // Hardened.Web.Runtime
        typeof(FromBodyAttribute), // Hardened.Requests.Abstract
        typeof(ConfigurationModelAttribute), // Hardened.Shared.Runtime
        typeof(IOptions<>), // Microsoft.Extensions.Options
    ];

    private static GeneratorResult Generate(string source) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            new IIncrementalGenerator[]
            {
                new LibrarySourceGenerator(),
                new WebLibrarySourceGenerator(),
            },
            Anchors
        );

    [Fact]
    public void AModelInTheHandlersNamespaceCompiles()
    {
        var result = Generate(
            """
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Microsoft.Extensions.Options;

            namespace TestApp;

            [ConfigurationModel]
            public partial class TodoListOptions
            {
                private int _pageSize = 20;
            }

            public class TodoController
            {
                [Get("/todos")]
                public int All(IOptions<ITodoListOptions> options) => options.Value.PageSize;
            }
            """
        );

        result.AssertNoErrors();

        Assert.Contains(
            result.GeneratedSources.Values,
            source => source.Contains("global::TestApp.ITodoListOptions")
        );
    }

    [Fact]
    public void AModelInAnImportedNamespaceCompiles()
    {
        var result = Generate(
            """
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Microsoft.Extensions.Options;
            using TestApp.Settings;

            namespace TestApp.Settings
            {
                [ConfigurationModel]
                public partial class TodoListOptions
                {
                    private int _pageSize = 20;
                }
            }

            namespace TestApp.Controllers
            {
                public class TodoController
                {
                    [Get("/todos")]
                    public int All(IOptions<ITodoListOptions> options) => options.Value.PageSize;
                }
            }
            """
        );

        result.AssertNoErrors();

        Assert.Contains(
            result.GeneratedSources.Values,
            source => source.Contains("global::TestApp.Settings.ITodoListOptions")
        );
    }
}
