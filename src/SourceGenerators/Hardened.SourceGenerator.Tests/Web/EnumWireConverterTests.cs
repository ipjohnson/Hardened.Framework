using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// The converters the build writes for a code-first application's enums, compiled rather than
/// string-matched.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary itself is covered in <c>Shared/EnumWireNamingTests</c> and the behaviour end to
/// end in <c>Hardened.IntegrationTests.WebApp.SUT</c>. These cover the step between: turning a
/// resolved vocabulary into C# that builds, in the same file as the routing table.
/// </para>
/// <para>
/// Held here as well as end to end because a generator runs during a build rather than inside a
/// test process. Everything it emits is exercised when the integration application compiles, and
/// none of that is the generator's own code being executed under test - so an emitter can be
/// entirely wrong in a way only a shipped application would show.
/// </para>
/// </remarks>
public class EnumWireConverterTests {

    private static string Application(string body) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Attributes;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class Application { }

        {{body}}
        """;

    private const string PriorityController = """
        public enum Priority { Low, InProgress }

        public record Ticket(Priority Priority);

        public class TicketController {
            [Get("/tickets")]
            public Ticket Get() => new(Priority.Low);
        }
        """;

    [Fact]
    public void AnEnumOnTheWireGetsAConverterCarryingItsValues() {
        var routing = RequestGeneratorHarness.Generate(Application(PriorityController))
            .AssertNoErrors()
            .SourceContaining("Application.Routing");

        Assert.Contains("class PriorityWireConverter", routing);
        Assert.Contains("=> \"inProgress\"", routing);
        Assert.Contains("\"inProgress\" => global::TestApp.Priority.InProgress", routing);
    }

    /// <summary>
    /// The binder's half. A path or query value is text and never reaches a JSON converter, so the
    /// same vocabulary has to be registered for it separately.
    /// </summary>
    [Fact]
    public void AnEnumOnTheWireGetsAStringConverterForTheBinder() {
        var routing = RequestGeneratorHarness.Generate(Application(PriorityController))
            .AssertNoErrors()
            .SourceContaining("Application.Routing");

        Assert.Contains("TryParseWire", routing);
        Assert.Contains("DelegatingStringConverter<global::TestApp.Priority>", routing);
    }

    [Fact]
    public void TheResolverAndTheStringConvertersAreRegistered() {
        var routing = RequestGeneratorHarness.Generate(Application(PriorityController))
            .AssertNoErrors()
            .SourceContaining("Application.Routing");

        Assert.Contains("IJsonTypeInfoResolver), global::TestApp.Application.JsonEnums.Resolver.Instance", routing);
        Assert.Contains("foreach (var stringConverter in global::TestApp.Application.JsonEnums.StringConverters)", routing);
    }

    [Fact]
    public void ADeclaredNamingOverridesTheDefault() {
        var routing = RequestGeneratorHarness.Generate(Application("""
            [JsonEnumNaming(EnumNaming.KebabCaseLower)]
            public enum Shipping { NextDay, TwoDay }

            public record Order(Shipping Shipping);

            public class OrderController {
                [Get("/orders")]
                public Order Get() => new(Shipping.NextDay);
            }
            """)).AssertNoErrors().SourceContaining("Application.Routing");

        Assert.Contains("=> \"next-day\"", routing);
        Assert.DoesNotContain("=> \"nextDay\"", routing);
    }

    /// <summary>
    /// An assembly-wide default, and one enum opting back out of it.
    /// </summary>
    [Fact]
    public void AnAssemblyDefaultAppliesAndAnEnumCanOptOut() {
        // Written out rather than through Application(), because an assembly attribute has to
        // precede every other element in the file - after the namespace it is CS1730.
        var routing = RequestGeneratorHarness.Generate("""
            using System;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            [assembly: JsonEnumNaming(EnumNaming.SnakeCaseUpper)]

            namespace TestApp;

            [HardenedModule]
            public partial class Application { }

            public enum Priority { InProgress }

            [JsonEnumNaming(EnumNaming.MemberName)]
            public enum LegacyCode { AB12 }

            public record Ticket(Priority Priority, LegacyCode Code);

            public class TicketController {
                [Get("/tickets")]
                public Ticket Get() => new(Priority.InProgress, LegacyCode.AB12);
            }
            """).AssertNoErrors().SourceContaining("Application.Routing");

        Assert.Contains("=> \"IN_PROGRESS\"", routing);
        Assert.Contains("=> \"AB12\"", routing);
    }

    /// <summary>
    /// A flags enum has no single member to name, and a framework enum's vocabulary is not this
    /// application's to redefine - so neither gets a converter.
    /// </summary>
    [Fact]
    public void AFlagsEnumIsLeftAlone() {
        var result = RequestGeneratorHarness.Generate(Application("""
            [Flags]
            public enum Access { None = 0, Read = 1, Write = 2 }

            public record Grant(Access Access);

            public class GrantController {
                [Get("/grants")]
                public Grant Get() => new(Access.Read);
            }
            """)).AssertNoErrors();

        Assert.DoesNotContain(
            result.GeneratedSources.Values, source => source.Contains("AccessWireConverter"));
    }

    [Fact]
    public void AFrameworkEnumIsLeftAlone() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public record Job(System.Threading.Tasks.TaskStatus Status);

            public class JobController {
                [Get("/jobs")]
                public Job Get() => new(System.Threading.Tasks.TaskStatus.Running);
            }
            """)).AssertNoErrors();

        Assert.DoesNotContain(
            result.GeneratedSources.Values, source => source.Contains("TaskStatusWireConverter"));
    }

    /// <summary>
    /// An application with no enum on the wire carries no container at all, rather than an empty
    /// one and two registrations that iterate nothing.
    /// </summary>
    [Fact]
    public void NoEnumOnTheWireEmitsNoContainer() {
        var result = RequestGeneratorHarness.Generate(Application("""
            public class PlainController {
                [Get("/plain")]
                public string Get() => "x";
            }
            """)).AssertNoErrors();

        Assert.DoesNotContain(
            result.GeneratedSources.Values, source => source.Contains("class JsonEnums"));
    }
}
