using System.Collections.Generic;
using System.Linq;
using Hardened.Idl.Models;
using Hardened.Idl.Validation;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The parameter interfaces and <c>[GeneratedRegex]</c> members a spec's constraints become.
/// </summary>
/// <remarks>
/// <para>
/// At <b>35% line coverage</b>. <c>OperationParametersTests</c> covers what goes into the model;
/// this covers what reaches the file.
/// </para>
/// <para>
/// <b><see cref="EmitPatterns"/> is why the build task exists at all.</b> A source generator cannot
/// emit <c>[GeneratedRegex]</c> — its output is not in the compilation the regex generator reads, so
/// the partial method is never implemented and the consumer's build fails with CS8795. And it is
/// written as raw text rather than through <c>MethodDefinition</c>, which always writes a body: a
/// partial method with <c>{ }</c> is SYSLIB1043. Both mistakes produce a generated file that does
/// not compile, in somebody else's project.
/// </para>
/// </remarks>
public class ValidationEmitterTests {

    private const string ValidationNamespace = EmitterHarness.RootNamespace + ".Validation";

    private static PatternRegistry Patterns() => new(ValidationNamespace, "petstore");

    private static ParameterModel Parameter(
        string name = "petId", int? minLength = null, string? pattern = null) =>
        new() { Name = name, In = "path", Type = "string", MinLength = minLength, Pattern = pattern };

    private static ServiceSpecModel Spec(params OperationModel[] operations) =>
        new() {
            Services = [new ServiceModel { Tag = "pets", Operations = new List<OperationModel>(operations) }],
            Schemas = []
        };

    private static OperationModel Operation(string methodName, params ParameterModel[] parameters) =>
        new() {
            OperationId = methodName,
            MethodName = methodName,
            Path = "/pets",
            HttpMethod = "GET",
            Parameters = new List<ParameterModel>(parameters)
        };

    private static string EmitInterfaces(ServiceSpecModel spec, PatternRegistry? patterns = null) =>
        EmitterHarness.Write(
            ns => ValidationEmitter.Emit(
                ns, spec, EmitterHarness.ModelsNamespace, patterns ?? Patterns()),
            ValidationNamespace);

    private static IReadOnlyList<OperationParameters.Model> Models(
        ServiceSpecModel spec, PatternRegistry? patterns = null) {
        IReadOnlyList<OperationParameters.Model> result = [];

        EmitterHarness.Write(
            ns => result = ValidationEmitter.Emit(
                ns, spec, EmitterHarness.ModelsNamespace, patterns ?? Patterns()),
            ValidationNamespace);

        return result;
    }

    #region interfaces

    [Fact]
    public void AConstrainedOperationGetsAPublicPartialInterface() {
        Assert.Contains(
            "public partial interface IGetPetParameters",
            EmitInterfaces(Spec(Operation("GetPet", Parameter(minLength: 1)))));
    }

    [Fact]
    public void AnUnconstrainedOperationGetsNothing() {
        Assert.DoesNotContain(
            "interface", EmitInterfaces(Spec(Operation("GetPet", Parameter()))));
    }

    [Fact]
    public void EveryConstrainedOperationInTheSpecGetsOne() {
        var output = EmitInterfaces(Spec(
            Operation("GetPet", Parameter(minLength: 1)),
            Operation("ListPets", Parameter("limit", minLength: 2))));

        Assert.Contains("IGetPetParameters", output);
        Assert.Contains("IListPetsParameters", output);
    }

    [Fact]
    public void OnlyTheConstrainedOperationsAreEmitted() {
        var models = Models(Spec(
            Operation("GetPet", Parameter(minLength: 1)),
            Operation("ListPets", Parameter("limit"))));

        Assert.Equal(["IGetPetParameters"], models.Select(model => model.InterfaceName));
    }

    [Fact]
    public void EveryServiceInTheSpecContributes() {
        var spec = new ServiceSpecModel {
            Services = [
                new ServiceModel { Tag = "pets", Operations = [Operation("GetPet", Parameter(minLength: 1))] },
                new ServiceModel { Tag = "store", Operations = [Operation("GetOrder", Parameter("orderId", minLength: 1))] }
            ],
            Schemas = []
        };

        Assert.Equal(
            ["IGetPetParameters", "IGetOrderParameters"],
            Models(spec).Select(model => model.InterfaceName));
    }

    /// <summary>
    /// Get-only. The interface describes what the handler's <c>Parameters</c> class already has;
    /// declaring a setter would make the generated class have to provide one.
    /// </summary>
    [Fact]
    public void ThePropertiesAreGetOnly() {
        var output = EmitInterfaces(Spec(Operation("GetPet", Parameter(minLength: 1))));

        Assert.Contains("get;", output);
        Assert.DoesNotContain("set;", output);
    }

    [Fact]
    public void TheConstraintsReachTheProperty() {
        Assert.Contains(
            "StringLength",
            EmitInterfaces(Spec(Operation("GetPet", Parameter(minLength: 1)))));
    }

    [Fact]
    public void EveryParameterBecomesAProperty() {
        var output = EmitInterfaces(Spec(
            Operation("GetPet", Parameter("petId", minLength: 1), Parameter("name"))));

        Assert.Contains("petId", output);
        Assert.Contains("name", output);
    }

    #endregion

    #region the GeneratedRegex members

    private static string EmitPatterns(PatternRegistry patterns) =>
        EmitterHarness.Write(
            ns => ValidationEmitter.EmitPatterns(ns, patterns), ValidationNamespace);

    [Fact]
    public void NoPatternsEmitsNoClass() {
        Assert.DoesNotContain("PetstorePatterns", EmitPatterns(Patterns()));
    }

    [Fact]
    public void APatternBecomesAPartialClassNamedForTheSpec() {
        var patterns = Patterns();

        patterns.AttributeArguments("^[a-z]+$");

        Assert.Contains("internal static partial class PetstorePatterns", EmitPatterns(patterns));
    }

    /// <summary>
    /// A partial method with no body. <c>MethodDefinition</c> always writes one, so
    /// <c>ComponentModifier.Partial</c> on a method produces <c>static Regex P_x() { }</c> and
    /// SYSLIB1043 — which is why this half is raw text.
    /// </summary>
    [Fact]
    public void TheMemberIsABodylessPartialMethod() {
        var patterns = Patterns();

        patterns.AttributeArguments("^[a-z]+$");

        var output = EmitPatterns(patterns);

        Assert.Contains("public static partial global::System.Text.RegularExpressions.Regex P_c37a8736();", output);
        Assert.DoesNotContain("P_c37a8736() {", output);
    }

    [Fact]
    public void TheMemberCarriesGeneratedRegexWithThePattern() {
        var patterns = Patterns();

        patterns.AttributeArguments("^[a-z]+$");

        Assert.Contains(
            "[global::System.Text.RegularExpressions.GeneratedRegex(\"^[a-z]+$\")]",
            EmitPatterns(patterns));
    }

    /// <summary>
    /// A pattern is full of backslashes by nature, and it has to survive being written into a C#
    /// string literal.
    /// </summary>
    [Fact]
    public void ABackslashInThePatternIsEscaped() {
        var patterns = Patterns();

        patterns.AttributeArguments(@"^\d{3}$");

        Assert.Contains(@"GeneratedRegex(""^\\d{3}$"")", EmitPatterns(patterns));
    }

    [Fact]
    public void AQuoteInThePatternIsEscaped() {
        var patterns = Patterns();

        patterns.AttributeArguments("^\"[a-z]+\"$");

        Assert.Contains(@"GeneratedRegex(""^\""[a-z]+\""$"")", EmitPatterns(patterns));
    }

    [Fact]
    public void EveryDistinctPatternGetsItsOwnMember() {
        var patterns = Patterns();

        patterns.AttributeArguments("^[a-z]+$");
        patterns.AttributeArguments(@"^\d+$");

        var output = EmitPatterns(patterns);

        Assert.Equal(2, output.Split("GeneratedRegex").Length - 1);
    }

    /// <summary>
    /// A pattern the runtime refuses never reaches the file — emitted, it would fail to generate
    /// and leave its partial method unimplemented.
    /// </summary>
    [Fact]
    public void ARefusedPatternIsNotEmitted() {
        var patterns = Patterns();

        patterns.AttributeArguments(@"^[a-zA-Z0-9\-\_]+$");

        Assert.DoesNotContain("GeneratedRegex", EmitPatterns(patterns));
    }

    /// <summary>
    /// The class the constraint attributes point at and the class emitted here are the same name,
    /// or every <c>[Pattern]</c> reference is CS0234.
    /// </summary>
    [Fact]
    public void TheEmittedClassIsTheOneTheAttributesReference() {
        var patterns = Patterns();

        var arguments = patterns.AttributeArguments("^[a-z]+$");

        Assert.Contains($"class {patterns.ClassName}", EmitPatterns(patterns));
        Assert.Contains(patterns.ClassName, arguments![0]);
    }

    #endregion
}
