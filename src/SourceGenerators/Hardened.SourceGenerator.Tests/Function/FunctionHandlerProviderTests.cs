using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// The second output stage: the <c>FunctionHandlerProvider</c> nested in the application class, and
/// the dependency registration that makes it reachable.
///
/// <para>
/// The invokers are useless without this. It is the only thing that turns an incoming function name
/// into a handler, and the only place the handler classes themselves are registered — a handler that
/// generates perfectly and is never registered fails at run time with a DI resolution error rather
/// than at build time.
/// </para>
/// </summary>
public class FunctionHandlerProviderTests {

    private const string OneHandler = """
        [HardenedFunction]
        public void Process() { }
        """;

    private static string Provider(string body, string extraTypes = "") =>
        FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application(body, extraTypes))
            .AssertNoErrors()
            .SourceContaining("FunctionHandlers.cs");

    /// <summary>
    /// The emitted source with every run of whitespace removed. CSharpAuthor breaks a returned
    /// expression across lines and indents it oddly — <c>return</c>, a newline's worth of spaces,
    /// <c>null</c>, then a bare <c>;</c> — so asserting on the shape of a statement means asserting
    /// on something whitespace cannot move.
    /// </summary>
    private static string Compact(string source) =>
        string.Concat(source.Where(character => !char.IsWhiteSpace(character)));

    /// <summary>
    /// The file is named after the entry point, and declares the same class as a partial — the
    /// application class is where the registration has to land for DependencyRegistry to find it.
    /// </summary>
    [Fact]
    public void TheProviderIsEmittedAsAPartialOfTheApplicationClass() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application(OneHandler))
            .AssertNoErrors();

        Assert.Contains("TestApplication.FunctionHandlers.cs", result.GeneratedSources.Keys);
        Assert.Contains("public partial class TestApplication",
            result.SourceContaining("FunctionHandlers.cs"));
    }

    [Fact]
    public void TheProviderImplementsTheFunctionHandlerProviderInterface() {
        Assert.Contains(
            "class FunctionHandlerProvider : global::Hardened.Requests.Abstract.Execution.IFunctionHandlerProvider",
            Provider(OneHandler));
    }

    /// <summary>
    /// The provider is registered against the interface, as a singleton. Registered against its own
    /// concrete type instead, nothing resolving <c>IFunctionHandlerProvider</c> would find it.
    /// </summary>
    [Fact]
    public void TheProviderIsRegisteredAsASingletonAgainstItsInterface() {
        var provider = Provider(OneHandler);

        Assert.Contains("serviceCollection.AddSingleton<", provider);
        Assert.Contains("global::Hardened.Requests.Abstract.Execution.IFunctionHandlerProvider", provider);
        Assert.Contains("FunctionHandlerProvider", provider);
    }

    /// <summary>
    /// The class declaring the handler is registered too — the invoker resolves it out of the
    /// provider on every invocation, so without this the handler builds and then fails to resolve.
    /// </summary>
    [Fact]
    public void TheHandlerTypeIsRegisteredAsTransient() {
        Assert.Contains("serviceCollection.AddTransient<global::TestApp.TestFunctions>();",
            Provider(OneHandler));
    }

    /// <summary>
    /// Registration is hooked in through DependencyRegistry, keyed on the entry point, which is how
    /// DependencyModules discovers it. The <c>[DynamicDependency]</c> keeps the method from being
    /// trimmed — it is only ever called through the registry.
    /// </summary>
    [Fact]
    public void RegistrationIsAddedToTheApplicationsDependencyRegistry() {
        var provider = Provider(OneHandler);

        Assert.Contains("DependencyRegistry<TestApplication>.Add(FunctionHandlersDI)", provider);
        Assert.Contains("[DynamicDependency(nameof(FunctionHandlersDI))]", provider);
    }

    /// <summary>
    /// A handler with no explicit name answers to any function name. A Lambda that hosts one
    /// function never sends a name worth matching, which is the case SqsTest is.
    /// </summary>
    [Fact]
    public void AnUnnamedHandlerAnswersToAnyFunctionName() {
        var provider = Provider(OneHandler);

        Assert.Contains("return new global::TestApp.Generated.TestFunctions_Process(serviceProvider);", provider);
        Assert.DoesNotContain("switch (functionName)", provider);
    }

    /// <summary>
    /// An explicitly named handler is matched by name instead, through a switch.
    /// </summary>
    [Fact]
    public void ANamedHandlerIsMatchedByItsFunctionName() {
        var provider = Provider("""
            [HardenedFunction("order-received")]
            public void Process() { }
            """);

        Assert.Contains("switch (functionName)", provider);
        Assert.Contains("case \"order-received\":", provider);
    }

    /// <summary>
    /// A name that does not match any handler returns null rather than falling through to an
    /// arbitrary one. The caller distinguishes "no such function" from a handler that did nothing.
    /// </summary>
    [Fact]
    public void AnUnmatchedFunctionNameReturnsNull() {
        var provider = Compact(Provider("""
            [HardenedFunction("order-received")]
            public void Process() { }
            """));

        // The null return is the statement immediately after the switch closes, so a name matching
        // no case falls out of the switch and returns null rather than reaching any handler.
        Assert.Contains("}returnnull;", provider);
    }

    /// <summary>
    /// A named handler and an unnamed one together: the switch matches the named one first, and the
    /// unnamed one is the fallback for everything else.
    /// </summary>
    [Fact]
    public void ANamedHandlerIsMatchedBeforeTheUnnamedFallback() {
        var provider = Provider("""
            [HardenedFunction("order-received")]
            public void Named() { }

            [HardenedFunction]
            public void Fallback() { }
            """);

        var switchIndex = provider.IndexOf("case \"order-received\":", StringComparison.Ordinal);
        var fallbackIndex = provider.IndexOf("return new global::TestApp.Generated.TestFunctions_Fallback",
            StringComparison.Ordinal);

        Assert.True(switchIndex >= 0, "the named handler should be a switch case");
        Assert.True(fallbackIndex > switchIndex, "the unnamed handler should be the fallback after the switch");
    }

    /// <summary>
    /// The name may be a constant expression rather than a literal — the generator reads it through
    /// the semantic model, so a <c>const</c> resolves to its value rather than to its source text.
    /// </summary>
    [Fact]
    public void AFunctionNameGivenAsAConstantResolvesToItsValue() {
        var result = FunctionGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            public static class FunctionNames {
                public const string OrderReceived = "order-received";
            }

            [HardenedModule]
            public partial class TestApplication { }

            public class TestFunctions {
                [HardenedFunction(FunctionNames.OrderReceived)]
                public void Process() { }
            }
            """).AssertNoErrors();

        Assert.Contains("case \"order-received\":", result.SourceContaining("FunctionHandlers.cs"));
        Assert.Contains("order-received.FunctionHandler.cs", result.GeneratedSources.Keys);
    }

    /// <summary>
    /// A name expression the compiler cannot resolve falls back to the expression's source text
    /// rather than throwing. An editor runs generators over half-written code constantly, and a
    /// constant is unresolved for as long as it takes to declare it — the fallback keeps the rest of
    /// the compilation's generated code alive while that is true. The input itself does not compile,
    /// so this asserts on the generator's output rather than on a clean build.
    /// </summary>
    [Fact]
    public void AnUnresolvableFunctionNameFallsBackToItsSourceText() {
        var result = FunctionGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class TestFunctions {
                [HardenedFunction(NotDeclaredAnywhere.Name)]
                public void Process() { }
            }
            """);

        Assert.Empty(result.GeneratorExceptions);
        Assert.Contains("NotDeclaredAnywhere.Name.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("case \"NotDeclaredAnywhere.Name\":", result.SourceContaining("FunctionHandlers.cs"));
    }

    /// <summary>
    /// The function name is used verbatim as the generated file name, punctuation included. A name
    /// shaped like a path — which reads naturally for a namespaced queue or topic — becomes a hint
    /// name containing a separator. Roslyn accepts it, so this is recorded rather than raised:
    /// checked 2026-08-12, and the reason to know it is that anything writing these files to disk,
    /// as <c>EmitCompilerGeneratedFiles</c> does, is writing into a subdirectory.
    /// </summary>
    [Fact]
    public void TheFunctionNameIsUsedVerbatimAsTheGeneratedFileName() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction("orders/received")]
                public void Process() { }
            """)).AssertNoErrors();

        Assert.Contains("orders/received.FunctionHandler.cs", result.GeneratedSources.Keys);
        Assert.Contains("case \"orders/received\":", result.SourceContaining("FunctionHandlers.cs"));
    }

    /// <summary>
    /// Several handlers in one application. Each gets its own invoker file, each named function
    /// gets its own case, and the declaring class is registered once.
    /// </summary>
    [Fact]
    public void SeveralNamedHandlersEachGetTheirOwnCase() {
        var result = FunctionGeneratorHarness.Generate(FunctionGeneratorHarness.Application("""
                [HardenedFunction("first")] public void One() { }

                [HardenedFunction("second")] public void Two() { }

                [HardenedFunction("third")] public void Three() { }
            """)).AssertNoErrors();

        var provider = result.SourceContaining("FunctionHandlers.cs");

        Assert.Contains("case \"first\":", provider);
        Assert.Contains("case \"second\":", provider);
        Assert.Contains("case \"third\":", provider);

        Assert.Equal(4, result.GeneratedSources.Count);
    }

    /// <summary>
    /// Handlers spread across two classes. Both classes have to be registered — registering only
    /// the first would leave the second unresolvable at run time.
    /// </summary>
    [Fact]
    public void HandlersOnSeparateClassesEachRegisterTheirDeclaringType() {
        var provider = FunctionGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class OrderFunctions {
                [HardenedFunction("order")] public void Handle() { }
            }

            public class InvoiceFunctions {
                [HardenedFunction("invoice")] public void Handle() { }
            }
            """).AssertNoErrors().SourceContaining("FunctionHandlers.cs");

        Assert.Contains("serviceCollection.AddTransient<global::TestApp.OrderFunctions>();", provider);
        Assert.Contains("serviceCollection.AddTransient<global::TestApp.InvoiceFunctions>();", provider);
    }

    /// <summary>
    /// Two handlers on the same class register it once, not twice. A duplicate transient
    /// registration is not an error, but it doubles the descriptor list for every handler declared.
    /// </summary>
    [Fact]
    public void TwoHandlersOnOneClassRegisterItOnce() {
        var provider = Provider("""
            [HardenedFunction("first")] public void One() { }

            [HardenedFunction("second")] public void Two() { }
            """);

        var registrations = provider.Split("serviceCollection.AddTransient<global::TestApp.TestFunctions>();").Length - 1;

        Assert.Equal(1, registrations);
    }

    /// <summary>
    /// An application with no function handlers at all still gets a provider — one that returns
    /// null for every name. The registration is unconditional, so anything resolving
    /// <c>IFunctionHandlerProvider</c> finds an implementation rather than failing to construct.
    /// </summary>
    [Fact]
    public void AnApplicationWithNoHandlersStillRegistersAProviderThatReturnsNull() {
        var result = FunctionGeneratorHarness.Generate("""
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }
            """).AssertNoErrors();

        var provider = result.SourceContaining("FunctionHandlers.cs");

        Assert.Contains("global::Hardened.Requests.Abstract.Execution.IFunctionHandlerProvider", provider);
        Assert.DoesNotContain("AddTransient", provider);

        // Null for every name, with no switch and nothing to construct.
        var compact = Compact(provider);

        Assert.Contains("returnnull;", compact);
        Assert.DoesNotContain("switch(functionName)", compact);
        Assert.DoesNotContain("returnnew", compact);
    }

    /// <summary>
    /// Two application entry points in one compilation each get their own provider, each keyed on
    /// its own DependencyRegistry. The handlers are shared — they are selected per compilation, not
    /// per application.
    /// </summary>
    [Fact]
    public void EachEntryPointGetsItsOwnProvider() {
        var result = FunctionGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class AppOne { }

            [HardenedModule]
            public partial class AppTwo { }

            public class TestFunctions {
                [HardenedFunction] public void Process() { }
            }
            """).AssertNoErrors();

        Assert.Contains("AppOne.FunctionHandlers.cs", result.GeneratedSources.Keys);
        Assert.Contains("AppTwo.FunctionHandlers.cs", result.GeneratedSources.Keys);

        Assert.Contains("DependencyRegistry<AppOne>", result.GeneratedSources["AppOne.FunctionHandlers.cs"]);
        Assert.Contains("DependencyRegistry<AppTwo>", result.GeneratedSources["AppTwo.FunctionHandlers.cs"]);
    }

    /// <summary>
    /// The provider is emitted into the entry point's namespace, wherever the handlers live. The
    /// invoker types it constructs are referenced fully qualified, which is what lets the two sit in
    /// different namespaces at all.
    /// </summary>
    [Fact]
    public void TheProviderReferencesHandlersInOtherNamespacesFullyQualified() {
        var result = FunctionGeneratorHarness.Generate(new Dictionary<string, string> {
            ["Application.cs"] = """
                using Hardened.Shared.Runtime.Attributes;

                namespace TestApp;

                [HardenedModule]
                public partial class TestApplication { }
                """,
            ["Handlers.cs"] = """
                using Hardened.Requests.Abstract.Attributes;

                namespace TestApp.Handlers;

                public class OrderFunctions {
                    [HardenedFunction] public void Process() { }
                }
                """
        }).AssertNoErrors();

        var provider = result.SourceContaining("FunctionHandlers.cs");

        Assert.Contains("namespace TestApp", provider);
        Assert.Contains("global::TestApp.Handlers.Generated.OrderFunctions_Process", provider);
        Assert.Contains("serviceCollection.AddTransient<global::TestApp.Handlers.OrderFunctions>();", provider);
    }
}
