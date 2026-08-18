using CSharpAuthor;
using Hardened.Idl.Emitters;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// A schema or operation marked <c>deprecated</c>, as <c>[Obsolete]</c>.
/// </summary>
/// <remarks>
/// <para>
/// At <b>0% line coverage</b>. Twelve lines, and the whole of the reasoning is in whether the
/// second constructor argument is <c>false</c> and whether the pragma is there.
/// </para>
/// <para>
/// <b>Both are load-bearing.</b> A generated interface member carrying <c>[Obsolete]</c> produces
/// CS0618 wherever it is implemented — which for a spec-first project is the consumer's own handler,
/// code they did not write and cannot annotate away. This repository escalates warnings to errors
/// under <c>ContinuousIntegrationBuild</c> and consumers commonly do the same, so one deprecated
/// operation in a specification would break the build of every project implementing it. The pragma
/// is what stops that; <c>false</c> is what keeps it a warning rather than an error.
/// </para>
/// </remarks>
public class DeprecationTests {

    private static string Emit() =>
        EmitterHarness.Write(ns => {
            var definition = ns.AddClass("Pet");

            definition.Modifiers |= ComponentModifier.Public;

            Deprecation.Apply(definition);
        });

    [Fact]
    public void TheTypeIsMarkedObsolete() {
        Assert.Contains("Obsolete", Emit());
    }

    [Fact]
    public void TheMessageSaysWhereTheDeprecationCameFrom() {
        Assert.Contains("Declared deprecated by the specification.", Emit());
    }

    /// <summary>
    /// A warning, not an error. Deprecation is notice that something will go, not that it has gone.
    /// </summary>
    [Fact]
    public void TheObsoleteAttributeIsAWarningRatherThanAnError() {
        Assert.Contains("false", Emit());
        Assert.DoesNotContain("true", Emit());
    }

    /// <summary>
    /// Without this, one deprecated operation breaks the build of every project implementing it.
    /// </summary>
    [Fact]
    public void TheDeclarationIsWrappedInAPragmaSuppressing618() {
        var output = Emit();

        Assert.Contains("#pragma warning disable 618", output);
        Assert.Contains("#pragma warning restore 618", output);
    }

    [Fact]
    public void ThePragmaSurroundsTheDeclaration() {
        var output = Emit();

        var disable = output.IndexOf("#pragma warning disable 618", System.StringComparison.Ordinal);
        var declaration = output.IndexOf("class Pet", System.StringComparison.Ordinal);
        var restore = output.IndexOf("#pragma warning restore 618", System.StringComparison.Ordinal);

        Assert.InRange(declaration, disable, restore);
    }

    [Fact]
    public void AnUndeprecatedTypeCarriesNeither() {
        var output = EmitterHarness.Write(ns => {
            var definition = ns.AddClass("Pet");

            definition.Modifiers |= ComponentModifier.Public;
        });

        Assert.DoesNotContain("Obsolete", output);
        Assert.DoesNotContain("618", output);
    }
}
