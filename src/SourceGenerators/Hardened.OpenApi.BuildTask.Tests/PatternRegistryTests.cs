using Hardened.Idl.Validation;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The regular expressions a spec declares, and the <c>[GeneratedRegex]</c> member each becomes.
/// </summary>
/// <remarks>
/// <para>
/// The registry was at <b>21% line coverage</b>. It was constructed by the schema tests and asked
/// nothing, so neither the rejection path nor the deduplication had ever run.
/// </para>
/// <para>
/// Two properties carry real weight. <b>The member name has to be identical on every build</b> —
/// it is written into emitted source, so a name that moved would churn the file and recompile every
/// consumer; that is why the hash is FNV-1a rather than <c>string.GetHashCode</c>, which .NET Core
/// randomises per process. And <b>a pattern .NET cannot compile has to be refused here</b>, because
/// emitted anyway it reaches <c>[GeneratedRegex]</c>, fails to generate, and leaves its partial
/// method unimplemented — CS8795 in a generated file, for a pattern the document was entitled to
/// write.
/// </para>
/// </remarks>
public class PatternRegistryTests {

    private const string PatternNamespace = EmitterHarness.RootNamespace + ".Validation";

    private static PatternRegistry Registry(string specFileName = "petstore") =>
        new(PatternNamespace, specFileName);

    #region the class the members live on

    [Theory]
    [InlineData("petstore", "PetstorePatterns")]
    [InlineData("pet_store", "PetStorePatterns")]
    [InlineData("pet-store", "PetStorePatterns")]
    public void TheClassIsNamedForTheSpecFile(string specFileName, string expected) {
        Assert.Equal(expected, Registry(specFileName).ClassName);
    }

    [Fact]
    public void AFreshRegistryIsEmpty() {
        var registry = Registry();

        Assert.True(registry.IsEmpty);
        Assert.Empty(registry.Members);
        Assert.Empty(registry.Rejected);
    }

    [Fact]
    public void RegisteringAPatternMakesItNonEmpty() {
        var registry = Registry();

        registry.AttributeArguments("^[a-z]+$");

        Assert.False(registry.IsEmpty);
    }

    #endregion

    #region the reference form

    /// <summary>
    /// <c>[Pattern(typeof(X), nameof(X.Y))]</c> rather than <c>[Pattern("...")]</c>. The inline form
    /// makes the validation generator declare a <c>Regex</c> itself, which roots the parser and the
    /// interpreter — 448 KB on an AOT publish against 33 KB — and is what VM0017 rejects.
    /// </summary>
    [Fact]
    public void TheArgumentsAreATypeAndAMemberName() {
        var arguments = Registry().AttributeArguments("^[a-z]+$");

        Assert.NotNull(arguments);
        Assert.Equal(2, arguments!.Count);
        Assert.Equal($"typeof(global::{PatternNamespace}.PetstorePatterns)", arguments[0]);
        Assert.Equal($"nameof(global::{PatternNamespace}.PetstorePatterns.P_c37a8736)", arguments[1]);
    }

    /// <summary>
    /// Global-qualified, so the reference cannot bind to a consumer type of the same name.
    /// </summary>
    [Fact]
    public void TheTypeReferenceIsGlobalQualified() {
        var arguments = Registry().AttributeArguments("^[a-z]+$");

        Assert.All(arguments!, argument => Assert.Contains("global::", argument));
    }

    #endregion

    #region member naming

    /// <summary>
    /// Pinned literals, on purpose. Asserting only that two calls agree would pass for
    /// <c>string.GetHashCode</c>, which is stable within a process and different between them — and
    /// a name that changes between builds churns the emitted file and recompiles every consumer.
    /// These values are FNV-1a and must not move.
    /// </summary>
    [Theory]
    [InlineData("^[a-z]+$", "P_c37a8736")]
    [InlineData(@"^\d{3}-\d{4}$", "P_07d7974f")]
    [InlineData("abc", "P_1a47e90b")]
    public void MemberNamesAreAStableHashOfThePattern(string pattern, string expected) {
        var registry = Registry();

        registry.AttributeArguments(pattern);

        Assert.Equal(expected, Assert.Single(registry.Members).Value);
    }

    /// <summary>
    /// Two registries agree, which is the same property one build after another relies on.
    /// </summary>
    [Fact]
    public void TwoRegistriesNameThePatternIdentically() {
        Assert.Equal(
            Registry().AttributeArguments("^[a-z]+$"),
            Registry().AttributeArguments("^[a-z]+$"));
    }

    /// <summary>
    /// The member name does not depend on the spec file, only the class it hangs off does.
    /// </summary>
    [Fact]
    public void TheMemberNameIsIndependentOfTheSpecFile() {
        var first = Registry("petstore");
        var second = Registry("bank");

        first.AttributeArguments("^[a-z]+$");
        second.AttributeArguments("^[a-z]+$");

        Assert.Equal(
            Assert.Single(first.Members).Value, Assert.Single(second.Members).Value);
    }

    [Fact]
    public void EveryMemberNameIsAValidCSharpIdentifier() {
        var registry = Registry();

        foreach (var pattern in new[] { "^[a-z]+$", @"^\d+$", "[!@#$%^&*()]", "a|b" }) {
            registry.AttributeArguments(pattern);
        }

        Assert.All(registry.Members.Values, member => {
            Assert.StartsWith("P_", member);
            Assert.Equal(10, member.Length);
            Assert.All(member.Substring(2), character => Assert.True(Uri.IsHexDigit(character)));
        });
    }

    #endregion

    #region deduplication

    /// <summary>
    /// Identical patterns collapse onto one member, which is the reason names come from the pattern
    /// rather than from the property that declared it.
    /// </summary>
    [Fact]
    public void ThePatternDeclaredTwiceGetsOneMember() {
        var registry = Registry();

        var first = registry.AttributeArguments("^[a-z]+$");
        var second = registry.AttributeArguments("^[a-z]+$");

        Assert.Single(registry.Members);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentPatternsGetDifferentMembers() {
        var registry = Registry();

        registry.AttributeArguments("^[a-z]+$");
        registry.AttributeArguments(@"^\d+$");

        Assert.Equal(2, registry.Members.Count);
    }

    [Fact]
    public void MembersAreKeyedByPattern() {
        var registry = Registry();

        registry.AttributeArguments("^[a-z]+$");

        Assert.Equal("^[a-z]+$", Assert.Single(registry.Members).Key);
    }

    #endregion

    #region patterns .NET will not compile

    /// <summary>
    /// The case this exists for. OpenAPI specifies ECMA-262, and .NET's engine is not a superset:
    /// <c>\_</c> is an ordinary escaped underscore in ECMA-262 and an unrecognised escape here.
    /// Grafana's published spec declares one.
    /// </summary>
    [Fact]
    public void APatternDotNetCannotCompileIsRefused() {
        Assert.Null(Registry().AttributeArguments(@"^[a-zA-Z0-9\-\_]+$"));
    }

    [Fact]
    public void ARefusedPatternDeclaresNoMember() {
        var registry = Registry();

        registry.AttributeArguments(@"^[a-zA-Z0-9\-\_]+$");

        Assert.Empty(registry.Members);
        Assert.True(registry.IsEmpty);
    }

    /// <summary>
    /// Recorded with the reason, so the build can say what it dropped rather than silently
    /// generating a weaker model.
    /// </summary>
    [Fact]
    public void ARefusedPatternIsRecordedWithItsReason() {
        var registry = Registry();

        registry.AttributeArguments(@"^[a-zA-Z0-9\-\_]+$");

        var rejected = Assert.Single(registry.Rejected);

        Assert.StartsWith(@"^[a-zA-Z0-9\-\_]+$", rejected);
        Assert.Contains(" - ", rejected);
    }

    [Theory]
    [InlineData("(unclosed")]
    [InlineData("[unclosed")]
    [InlineData("a{2,1}")]
    [InlineData(@"\_")]
    public void EveryUncompilablePatternIsRefused(string pattern) {
        var registry = Registry();

        Assert.Null(registry.AttributeArguments(pattern));
        Assert.Single(registry.Rejected);
    }

    [Fact]
    public void TheSamePatternRefusedTwiceIsRecordedOnce() {
        var registry = Registry();

        registry.AttributeArguments(@"\_");
        registry.AttributeArguments(@"\_");

        Assert.Single(registry.Rejected);
    }

    /// <summary>
    /// A refused pattern that is a prefix of one already refused is still reported.
    /// </summary>
    /// <remarks>
    /// It was not, until 2026-08-18. Entries are <c>pattern + " - " + message</c> and the
    /// deduplication asked whether any entry <em>started with</em> the pattern, so refusing
    /// <c>\_x</c> and then <c>\_</c> matched an unrelated entry and dropped the second. Both were
    /// refused either way; the build simply understated what it had dropped, which is the one job
    /// this list has.
    /// </remarks>
    [Fact]
    public void APatternThatIsAPrefixOfAnAlreadyRefusedOneIsStillReported() {
        var registry = Registry();

        Assert.Null(registry.AttributeArguments(@"\_x"));
        Assert.Null(registry.AttributeArguments(@"\_"));

        Assert.Equal(2, registry.Rejected.Count);
    }

    [Fact]
    public void RefusalsAreReportedInTheOrderTheyWereSeen() {
        var registry = Registry();

        registry.AttributeArguments(@"\_");
        registry.AttributeArguments("(unclosed");

        Assert.Equal(2, registry.Rejected.Count);
        Assert.StartsWith(@"\_", registry.Rejected[0]);
        Assert.StartsWith("(unclosed", registry.Rejected[1]);
    }

    /// <summary>
    /// A refusal does not disturb the patterns that did compile.
    /// </summary>
    [Fact]
    public void AGoodPatternStillRegistersAlongsideARefusedOne() {
        var registry = Registry();

        registry.AttributeArguments("^[a-z]+$");
        registry.AttributeArguments(@"\_");
        registry.AttributeArguments(@"^\d+$");

        Assert.Equal(2, registry.Members.Count);
        Assert.Single(registry.Rejected);
    }

    #endregion
}
