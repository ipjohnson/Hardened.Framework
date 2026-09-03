using CSharpAuthor;
using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Models.Request;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// A service typed as its concrete class, and the request body it silently became.
///
/// <para>
/// CS-08. The build failed as a CS7036 in a generated file, one hop from the parameter that
/// changed meaning and naming neither it nor the convention that decided it.
/// </para>
/// </summary>
public class ServiceParameterDiagnosticsTests {
    private const string DiagnosticId = "HRDR007";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(FromBodyAttribute)   // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string types, string parameters) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using Hardened.Requests.Abstract.Attributes;
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class TestApplication { }

                    public interface IClock { }

                    {{types}}

                    public class EventController {
                        [Post("/events")]
                        public string Handle({{parameters}}) => "";
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private static Diagnostic? Reported(string types, string parameters) =>
        Generate(types, parameters).GeneratorDiagnostics
            .SingleOrDefault(reported => reported.Id == DiagnosticId);

    private const string Store = """
        public class EventStore {
            public EventStore(IClock clock) { }
        }
        """;

    [Fact]
    public void AConcreteServiceParameterIsReported() {
        var diagnostic = Reported(Store, "EventStore store");

        Assert.NotNull(diagnostic);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// The message has to carry what the generated file could not: which parameter, which type,
    /// which convention, and both ways out.
    /// </summary>
    [Fact]
    public void TheMessageNamesTheParameterTheTypeAndBothFixes() {
        var message = Reported(Store, "EventStore store")!.GetMessage();

        Assert.Contains("'store'", message);
        Assert.Contains("EventStore", message);
        Assert.Contains("EventController", message);
        Assert.Contains("Handle", message);
        Assert.Contains("[FromServices]", message);
        Assert.Contains("interface", message);
    }

    /// <summary>
    /// Every shape that is a body and must stay one. A class the deserializer can construct is not
    /// reported however many services sit beside it in the signature.
    /// </summary>
    [Theory]
    // A plain body model.
    [InlineData("public class EventBody { public string Title { get; set; } = \"\"; }",
        "EventBody body")]
    // An immutable body model, whose constructor takes its own data rather than a service.
    [InlineData("public class EventBody { public EventBody(string title) { Title = title; } " +
                "public string Title { get; } }",
        "EventBody body")]
    // A record, which System.Text.Json constructs through its primary constructor.
    [InlineData("public record EventBody(string Title);", "EventBody body")]
    // One constructor takes a service, another does not.
    [InlineData("public class EventBody { public EventBody() { } public EventBody(IClock c) { } }",
        "EventBody body")]
    // The correct spelling of the reported case: still a service, no longer the body.
    [InlineData("public class EventStore { public EventStore(IClock clock) { } }",
        "[FromServices] EventStore store")]
    // And the same service reached through its interface.
    [InlineData("public interface IEventStore { }", "IEventStore store")]
    public void AParameterThatCanBeABodyIsLeftAlone(string types, string parameters) =>
        Assert.Null(Reported(types, parameters));
}
