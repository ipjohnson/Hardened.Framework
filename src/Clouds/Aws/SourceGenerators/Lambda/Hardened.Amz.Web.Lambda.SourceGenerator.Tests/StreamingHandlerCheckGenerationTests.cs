using Xunit;

namespace Hardened.Amz.Web.Lambda.SourceGenerator.Tests;

/// <summary>
/// The list of event-stream handlers the application hands to
/// <c>StreamingHandlerCheck.Warn</c> at startup.
///
/// <para>
/// The build cannot refuse server-sent events on a buffered deployment, because the same assembly
/// serves both modes and the mode is an environment variable. What it can do is name the handlers
/// that stream, so the running application can say at startup when its deployment cannot deliver
/// them. The scan matches the attribute by metadata name rather than by spelling, since the list
/// names handlers to an operator and a lookalike in another namespace would name the wrong one.
/// </para>
/// </summary>
public class StreamingHandlerCheckGenerationTests {

    private const string FeedController = """
        using System.Collections.Generic;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp.Controller;

        public class FeedController {
            [Get("/feed")]
            [ServerSentEvents]
            public IAsyncEnumerable<string> Feed() => throw new System.NotImplementedException();

            [Get("/items")]
            public IAsyncEnumerable<string> Items() => throw new System.NotImplementedException();
        }
        """;

    private static string Application(params string[] additionalSources) =>
        WebGeneratorHarness.Generate(WebGeneratorHarness.Application(), additionalSources).SourceContaining("App");

    /// <summary>
    /// An application with no event-stream handlers still makes the call, with nothing in the
    /// list, so the generated constructor has one shape whatever the controllers do.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoEventStreamHandlersHandsOverAnEmptyList() {
        var application = Application();

        Assert.Contains(
            "private static readonly string[] _streamingHandlers = global::System.Array.Empty<string>();",
            application);
        WebGeneratorHarness.AssertEmits(application,
            "global::Hardened.Amz.Web.Lambda.Runtime.Impl.StreamingHandlerCheck.Warn(" +
            "RootServiceProvider, _streamingHandlers);");
    }

    /// <summary>
    /// A handler carrying <c>[ServerSentEvents]</c> is named by type and method. One that streams
    /// NDJSON is not: it arrives late on a buffered deployment but intact, and the check is for the
    /// stream a client would give up on.
    /// </summary>
    [Fact]
    public void AServerSentEventsHandlerIsNamedForTheStartupCheck() {
        var application = Application(FeedController);

        WebGeneratorHarness.AssertEmits(application,
            "private static readonly string[] _streamingHandlers = " +
            "new string[] { \"TestApp.Controller.FeedController.Feed\" };");
        Assert.DoesNotContain("FeedController.Items", application);
    }

    /// <summary>
    /// The check reads the response mode from the container, so it runs after the container is
    /// built and after the event processor - the last thing the constructor resolves - is in place.
    /// </summary>
    [Fact]
    public void TheCheckRunsAfterTheEventProcessorIsResolved() {
        var application = Application(FeedController);

        var processor = application.IndexOf("_eventProcessor = RootServiceProvider", StringComparison.Ordinal);
        var check = application.IndexOf("StreamingHandlerCheck.Warn(", StringComparison.Ordinal);

        Assert.True(processor >= 0 && check > processor, "the check runs before the event processor is resolved");
    }

    /// <summary>
    /// Sorted, so the emitted file is the same whatever order the compilation walked the handlers
    /// in. An order that followed the walk would regenerate the application on every edit that
    /// moved a controller.
    /// </summary>
    [Fact]
    public void HandlersAreListedInAStableOrder() {
        var application = Application(
            """
            using System.Collections.Generic;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp.Controller;

            public class ZebraController {
                [Get("/zebra")]
                [ServerSentEvents]
                public IAsyncEnumerable<string> Watch() => throw new System.NotImplementedException();
            }

            public class AlphaController {
                [Get("/alpha")]
                [ServerSentEvents]
                public IAsyncEnumerable<string> Watch() => throw new System.NotImplementedException();
            }
            """);

        WebGeneratorHarness.AssertEmits(application,
            "new string[] { \"TestApp.Controller.AlphaController.Watch\", \"TestApp.Controller.ZebraController.Watch\" };");
    }

    /// <summary>
    /// The scan resolves the attribute's symbol. An attribute that merely shares the name does not
    /// mark a handler, which is the difference from the module selectors this generator used to
    /// have, which compared spellings.
    /// </summary>
    [Fact]
    public void AnAttributeThatOnlySharesTheNameDoesNotMarkAHandler() {
        var application = Application(
            """
            using System.Collections.Generic;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp.Controller;

            public class ServerSentEventsAttribute : System.Attribute { }

            public class LookalikeController {
                [Get("/lookalike")]
                [ServerSentEvents]
                public IAsyncEnumerable<string> Watch() => throw new System.NotImplementedException();
            }
            """);

        Assert.Contains("_streamingHandlers = global::System.Array.Empty<string>();", application);
    }
}
