using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// An imported module's links are constructed through a fully qualified name.
/// </summary>
/// <remarks>
/// The property's type comes from CSharpAuthor and is qualified already; the constructor call
/// beside it was built by hand and was not. An application named for a type in its own root
/// namespace is where that shows: <c>Todo.Host</c> importing <c>Todo.TodoLibrary</c>, in an
/// assembly that also declares <c>record Todo</c>, binds the leading <c>Todo</c> to the record
/// rather than the namespace and fails with CS0426. That is the shape
/// <c>dotnet new hardened-web -n Todo</c> produces, so the name most likely to be typed for a todo
/// sample was the one name that could not build.
/// </remarks>
public class ImportedLinkQualificationTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(FromBodyAttribute)   // Hardened.Requests.Abstract
    ];

    /// <summary>
    /// A library publishing links, and a type sharing the root namespace's name beside them.
    /// </summary>
    /// <remarks>
    /// Written out rather than generated. The host generator resolves an imported links type by
    /// metadata name against the compilation, so what matters here is that the reference contains
    /// <c>Todo.TodoLibrary+Links</c> and <c>Todo.Todo</c> - not how they got there. Generating them
    /// would test the library half of the generator on the way to the host half.
    /// </remarks>
    private const string Library =
        """
        using System;
        using Hardened.Web.Runtime.Links;

        namespace Todo;

        public record Todo(int Id, string Title);

        public class TodoLibraryAttribute : Attribute { }

        public partial class TodoLibrary {
            public sealed class Links {
                public Links(ILinkContext context) { }
            }
        }
        """;

    private const string Host =
        """
        using Hardened.Shared.Runtime.Attributes;
        using Todo;

        namespace Todo.Host;

        [HardenedModule]
        [TodoLibrary]
        public partial class Application { }
        """;

    [Fact]
    public void AnImportedLinkIsQualifiedAgainstATypeSharingTheRootNamespaceName() {
        var library = GeneratorTestHarness.CompileLibrary(Library, "Todo", Anchors);

        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Application.cs"] = Host },
            [new WebLibrarySourceGenerator()],
            Anchors,
            additionalReferences: [library.Reference]);

        Assert.DoesNotContain(
            result.Errors,
            diagnostic => diagnostic.Id == "CS0426");

        Assert.DoesNotContain(
            result.Errors,
            diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
