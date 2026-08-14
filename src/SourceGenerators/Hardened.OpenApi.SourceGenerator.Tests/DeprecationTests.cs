using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// <c>deprecated</c> reaching the generated code as <c>[Obsolete]</c>.
/// </summary>
public class DeprecationTests {

    /// <summary>
    /// The point of the pragma. A consumer implementing a deprecated operation would otherwise get
    /// CS0618 in code they did not write — an error, not a warning, anywhere
    /// <c>TreatWarningsAsErrors</c> is on, which is most CI.
    /// </summary>
    [Fact]
    public void ADeprecatedSpecStillCompilesForItsConsumer() {
        OpenApiGenerator.Run(
                Specs.Deprecated,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class ThingServiceImpl : IThingService {
                        public Task<OldThing> ListThings() => Task.FromResult(new OldThing("1"));
                    }
                    """))
            .AssertNoErrors();
    }

    [Fact]
    public void ADeprecatedOperationAndSchemaAreMarkedObsolete() {
        var generated = OpenApiGenerator.Run(Specs.Deprecated).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("Obsolete(\"Declared deprecated by the specification.\", false)", generated);
        Assert.Contains("#pragma warning disable 618", generated);
        Assert.Contains("#pragma warning restore 618", generated);
    }

    /// <summary>A spec that deprecates nothing carries no obsolete markers at all.</summary>
    [Fact]
    public void AnUndeprecatedSpecIsUnmarked() {
        var generated = OpenApiGenerator.Run(Specs.Minimal).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.DoesNotContain("Obsolete", generated);
        Assert.DoesNotContain("618", generated);
    }
}
