using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The generator's copy of the response-model enum against the public one.
///
/// <para>
/// There are two enums on purpose: the generator targets <c>netstandard2.0</c> and references no
/// Hardened runtime package, so it reads the attribute as source text and never loads the type that
/// declares it. What that buys in isolation it costs in the ability to drift - a member added to
/// one and not the other produces a generator that silently reads the new mode as Standard, which
/// is the failure mode this whole feature is arranged to avoid.
/// </para>
///
/// <para>
/// Asserted by name rather than by ordinal in both directions, because the selector matches on the
/// member name. Renaming a member on either side is the change that breaks it, and renaming a
/// public enum member is a wire-visible act that should have to be done twice deliberately.
/// </para>
/// </summary>
public class ResponseModelParityTests {

    [Fact]
    public void TheTwoEnums_DeclareTheSameMembers() {
        var publicNames = Enum.GetNames(typeof(Hardened.Requests.Abstract.Responses.ResponseModel));
        var generatorNames = Enum.GetNames(typeof(ResponseModelValue));

        Assert.Equal(publicNames.OrderBy(n => n), generatorNames.OrderBy(n => n));
    }

    /// <summary>
    /// Standard has to be the zero value on both sides. It is what an entry point that says nothing
    /// gets, and <c>default(ResponseModel)</c> is what an attribute reader lands on when anything
    /// upstream fails to supply a value.
    /// </summary>
    [Fact]
    public void Standard_IsTheDefaultOnBothSides() {
        Assert.Equal(
            Hardened.Requests.Abstract.Responses.ResponseModel.Standard,
            default(Hardened.Requests.Abstract.Responses.ResponseModel));

        Assert.Equal(ResponseModelValue.Standard, default(ResponseModelValue));
        Assert.Equal(ResponseModelValue.Standard, ResponseModelSelector.Default);
    }
}
