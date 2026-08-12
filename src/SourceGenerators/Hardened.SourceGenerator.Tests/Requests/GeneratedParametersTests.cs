using System.Reflection;
using Hardened.Requests.Abstract.Execution;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The generated <c>Parameters</c> bag, compiled, loaded and actually called.
///
/// <para>
/// Every other test in this suite reads the emitted source. That is enough to prove the generator
/// wrote the characters expected of it, and not enough to prove the result behaves: the indexer
/// setter shipped for the life of the project ending each case in <c>break</c>, which leaves the
/// switch and falls into the <c>IndexOutOfRangeException</c> below it, so setting a <em>valid</em>
/// index threw exactly like an invalid one. It compiled, it read correctly, and no test drove it.
/// Fixed 2026-08-12; this suite exists so it cannot come back.
/// </para>
///
/// <para>
/// It also covers the bag through <see cref="ExecutionRequestParameters"/>, which now supplies
/// lookup by name, the count and cloning for every handler in an application at once.
/// </para>
/// </summary>
public class GeneratedParametersTests {

    // Both parameters are types the harness can already resolve. Naming an undeclared type here
    // does not produce a diagnostic - it crashes the generator with a NullReferenceException,
    // which is the same unresolvable-type behaviour recorded in TESTING-PLAN.md section 12.
    private const string TwoParameterController = """
        [Post("/orders/{id}")]
        public string Save(string id, string name) => id + name;
        """;

    private static Type ParametersType() {
        var result = RequestGeneratorHarness
            .Generate(RequestGeneratorHarness.Controller(TwoParameterController))
            .AssertNoErrors();

        using var stream = new MemoryStream();

        var emit = result.Compilation.Emit(stream);

        Assert.True(emit.Success,
            "The generated code compiled but could not be emitted:" + Environment.NewLine +
            string.Join(Environment.NewLine, emit.Diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));

        var assembly = Assembly.Load(stream.ToArray());

        var parameters = assembly.GetTypes()
            .FirstOrDefault(type => type.Name == "Parameters" && type.DeclaringType != null);

        Assert.True(parameters != null,
            "No generated Parameters type. Types: " +
            string.Join(", ", assembly.GetTypes().Select(type => type.FullName)));

        return parameters!;
    }

    private static IExecutionRequestParameters New() =>
        (IExecutionRequestParameters)Activator.CreateInstance(ParametersType())!;

    /// <summary>
    /// The case this suite exists for. Every valid index used to throw.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SettingAValidIndexStoresTheValue(int index) {
        var parameters = New();

        parameters[index] = "a value";

        Assert.Equal("a value", parameters[index]);
    }

    [Fact]
    public void EachIndexKeepsItsOwnValue() {
        var parameters = New();

        parameters[0] = "an id";
        parameters[1] = "a name";

        Assert.Equal("an id", parameters[0]);
        Assert.Equal("a name", parameters[1]);
    }

    [Fact]
    public void AnIndexPastTheEndStillThrows() {
        var parameters = New();

        Assert.Throws<IndexOutOfRangeException>(() => parameters[2] = "past the end");
        Assert.Throws<IndexOutOfRangeException>(() => _ = parameters[2]);
    }

    [Fact]
    public void ParameterCountMatchesWhatTheHandlerDeclares() {
        Assert.Equal(2, New().ParameterCount);
    }

    [Fact]
    public void InfoNamesTheParametersInDeclarationOrder() {
        var info = New().Info;

        Assert.Equal(2, info.Count);
        Assert.Equal("id", info[0].Name);
        Assert.Equal("name", info[1].Name);
        Assert.Equal(0, info[0].Index);
        Assert.Equal(1, info[1].Index);
    }

    [Fact]
    public void AParameterIsReachableByTheNameItWasDeclaredWith() {
        var parameters = New();

        Assert.True(parameters.TrySetParameter("id", "an id"));
        Assert.True(parameters.TryGetParameter("id", out var value));
        Assert.Equal("an id", value);
    }

    [Fact]
    public void AnUnknownNameIsRefusedRatherThanThrowing() {
        var parameters = New();

        Assert.False(parameters.TrySetParameter("absent", "ignored"));
        Assert.False(parameters.TryGetParameter("absent", out var value));
        Assert.Null(value);
    }

    /// <summary>
    /// The isolation <c>IExecutionRequest.Clone</c> depends on, proven against a real generated
    /// bag rather than a stand-in — the generator emits one property per parameter, which is the
    /// shape <c>MemberwiseClone</c> can actually detach.
    /// </summary>
    [Fact]
    public void CloningABagDetachesItFromTheOriginal() {
        var parameters = New();

        parameters[0] = "original";

        var clone = parameters.Clone();

        Assert.NotSame(parameters, clone);
        Assert.Equal("original", clone[0]);

        clone[0] = "rebound";

        Assert.Equal("original", parameters[0]);
    }
}
