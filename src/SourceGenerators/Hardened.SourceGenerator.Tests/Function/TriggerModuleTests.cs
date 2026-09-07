using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// Which payload adapter a project's triggers register, and what happens when nothing declares one.
///
/// <para>
/// The generator knows the trigger attributes and nothing else. Which module serves each comes from
/// the runtime package's own build properties, which is what lets the same handler run on another
/// provider by changing a package reference - so every case here supplies those properties the way
/// a referenced runtime would.
/// </para>
/// </summary>
public class TriggerModuleTests {

    /// <summary>
    /// Real modules, standing in for the ones a cloud runtime would name.
    /// </summary>
    /// <remarks>
    /// Chosen from assemblies this suite already references so the emitted registration can be
    /// compiled rather than only matched as text - which is the assertion that matters, since the
    /// module arrives as a string from MSBuild and a generator that spelled the call wrong would
    /// pass every text comparison. The generator has no opinion about which module a property
    /// names, so using the real CoreModule here would test nothing extra and would put a cloud
    /// package in the generator suite's references.
    /// </remarks>
    private const string CoreModule = "Hardened.Shared.Runtime.DependencyInjection.HardenedCoreModule";

    private const string RequestModule = "Hardened.Requests.Runtime.DependencyInjection.HardenedRequestModule";

    private const string TimeoutModule = "Hardened.Requests.Runtime.Filters.RequestTimeouts";

    private static GeneratorResult Generate(string handlers, params (string, string)[] properties) {
        var source = $$"""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Functions.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class Handlers {
            {{handlers}}
            }
            """;

        return GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            [new FunctionGenerator()],
            FunctionGeneratorHarness.Anchors,
            buildProperties: properties.ToDictionary(pair => pair.Item1, pair => pair.Item2));
    }

    /// <summary>
    /// Runs the generator and compiles what it wrote. Anything the generator emits that does not
    /// bind - a mis-spelled call, a missing global::, a module named in a way C# will not accept -
    /// fails here rather than in a consumer's build.
    /// </summary>
    private static GeneratorResult GenerateAndCompile(
        string handlers, params (string, string)[] properties) =>
        Generate(handlers, properties).AssertNoErrors();

    private static string Registration(GeneratorResult result) =>
        result.GeneratedSources.TryGetValue("TestApplication.TriggerModules.cs", out var source)
            ? source
            : "";

    [Fact]
    public void AQueueHandlerRegistersTheModuleTheRuntimeNames() {
        var result = GenerateAndCompile(
            """
                [Queue("orders-new")]
                public void OnOrder(string body) { }
            """,
            ("HardenedQueueModule", CoreModule));

        Assert.Contains("AddModule(new global::" + CoreModule + "())", Registration(result));
    }

    /// <summary>
    /// The whole point of the property. The same handler, a different runtime, a different adapter,
    /// and nothing in the handler changes.
    /// </summary>
    [Fact]
    public void TheSameHandlerRegistersWhateverTheRuntimeNames() {
        var result = Generate(
            """
                [Queue("orders-new")]
                public void OnOrder(string body) { }
            """,
            ("HardenedQueueModule", "Contoso.Bus.QueueModule"));

        // Not compiled, and this is the one case that cannot be: the point is that the generator
        // emits whatever the property names without knowing the type, so naming a type this suite
        // could resolve would be testing the opposite of the property's purpose.
        Assert.Contains("AddModule(new global::Contoso.Bus.QueueModule())", Registration(result));
    }

    /// <summary>
    /// Registered into DependencyRegistry keyed on the entry point, which is the seam that needs no
    /// cooperation from the DependencyModules generator - its InternalGetModules already combines
    /// what the attributes declared with whatever was added there.
    /// </summary>
    [Fact]
    public void TheRegistrationIsKeyedOnTheEntryPoint() {
        var result = GenerateAndCompile(
            """
                [Queue("orders-new")]
                public void OnOrder(string body) { }
            """,
            ("HardenedQueueModule", CoreModule));

        Assert.Contains("DependencyRegistry<global::TestApp.TestApplication>", Registration(result));
    }

    /// <summary>
    /// A field rather than a method, because a field initializer needs nothing to call it - and a
    /// static constructor is not available, the DependencyModules generator having already emitted
    /// one on this class.
    /// </summary>
    [Fact]
    public void TheRegistrationIsAStaticFieldInitializer() {
        var registration = Registration(GenerateAndCompile(
            """
                [Queue("orders-new")]
                public void OnOrder(string body) { }
            """,
            ("HardenedQueueModule", CoreModule)));

        Assert.Contains("private static int _hardenedTriggerModules", registration);
        Assert.DoesNotContain("static TestApplication(", registration);
    }

    /// <summary>
    /// A schedule and a bus event are one adapter, so naming it twice must not register it twice -
    /// two adapters would both claim every EventBridge payload.
    /// </summary>
    [Fact]
    public void TwoTriggersSharingAModuleRegisterItOnce() {
        var registration = Registration(GenerateAndCompile(
            """
                [Timer("nightly")]
                public void Nightly() { }

                [Event("com.acme.orders", "OrderPlaced")]
                public void OnPlaced(string body) { }
            """,
            ("HardenedTimerModule", TimeoutModule),
            ("HardenedEventModule", TimeoutModule)));

        var first = registration.IndexOf(TimeoutModule, System.StringComparison.Ordinal);

        Assert.True(first > -1, "the shared module should be registered");
        Assert.Equal(
            first,
            registration.LastIndexOf(TimeoutModule, System.StringComparison.Ordinal));
    }

    [Fact]
    public void SeveralTriggersRegisterSeveralModules() {
        var registration = Registration(GenerateAndCompile(
            """
                [Queue("orders-new")]
                public void OnOrder(string body) { }

                [Topic("order-events")]
                public void OnEvent(string body) { }
            """,
            ("HardenedQueueModule", CoreModule),
            ("HardenedTopicModule", RequestModule)));

        Assert.Contains(CoreModule, registration);
        Assert.Contains(RequestModule, registration);
    }

    /// <summary>
    /// No runtime package, so nothing is registered and nothing is reported. A handler library
    /// with no cloud referenced is an ordinary thing to compile.
    /// </summary>
    [Fact]
    public void AProjectWithNoRuntimeIsSilent() {
        var result = Generate("""
                [Queue("orders-new")]
                public void OnOrder(string body) { }
            """);

        Assert.DoesNotContain("TestApplication.TriggerModules.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.GeneratorDiagnostics);
    }

    /// <summary>
    /// A project with no trigger gets no file at all, rather than one registering nothing.
    /// </summary>
    [Fact]
    public void AProjectWithNoTriggerGeneratesNothing() {
        var result = Generate("""
                public void NotAHandler() { }
            """);

        Assert.DoesNotContain("TestApplication.TriggerModules.cs", result.GeneratedSources.Keys);
        Assert.Empty(result.GeneratorDiagnostics);
    }

    /// <summary>
    /// The failure this diagnostic exists for: a runtime is referenced and does not serve one of
    /// the triggers used. Without it the handler compiles, deploys, and is never invoked - the
    /// hardest kind of thing to find, because everything looks right.
    /// </summary>
    [Fact]
    public void ATriggerNothingBindsIsReported() {
        var result = Generate(
            """
                [Queue("orders-new")]
                public void OnOrder(string body) { }

                [Timer("nightly")]
                public void Nightly() { }
            """,
            ("HardenedQueueModule", CoreModule));

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);

        Assert.Equal("HRDF001", diagnostic.Id);
        Assert.Contains("HardenedTimerModule", diagnostic.GetMessage());
        Assert.Contains("Timer", diagnostic.GetMessage());
    }

    /// <summary>
    /// One unbound trigger does not cost the others their registration: the report names what is
    /// missing and the rest of the project still works.
    /// </summary>
    [Fact]
    public void AnUnboundTriggerDoesNotStopTheBoundOnes() {
        var result = Generate(
            """
                [Queue("orders-new")]
                public void OnOrder(string body) { }

                [Topic("order-events")]
                public void OnEvent(string body) { }
            """,
            ("HardenedQueueModule", CoreModule));

        Assert.Contains(CoreModule, Registration(result));
        Assert.Single(result.GeneratorDiagnostics);
    }

    /// <summary>
    /// Attributes are matched on how they are written, the way SyntaxSelector does, so every legal
    /// spelling of the same attribute has to reach the same module.
    /// </summary>
    [Theory]
    [InlineData("Queue")]
    [InlineData("QueueAttribute")]
    [InlineData("Hardened.Functions.Runtime.Attributes.QueueAttribute")]
    public void ATriggerIsFoundUnderAnySpelling(string written) {
        var result = GenerateAndCompile(
            $$"""
                [{{written}}("orders-new")]
                public void OnOrder(string body) { }
            """,
            ("HardenedQueueModule", CoreModule));

        Assert.Contains(CoreModule, Registration(result));
    }
}
