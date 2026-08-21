using CSharpAuthor;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Reading <c>[ResponseModel(...)]</c> off an entry point.
///
/// <para>
/// The value decides what the invoke method is emitted as, so misreading it produces an application
/// generated in the wrong model - which compiles either way. The cases below are the spellings a
/// user can actually write, because the attribute arrives here as source text rather than as a
/// resolved symbol.
/// </para>
/// </summary>
public class ResponseModelSelectorTests {

    #region defaulting

    /// <summary>
    /// Every application built before this attribute existed says nothing, and must keep building
    /// exactly as it did.
    /// </summary>
    [Fact]
    public void Read_DefaultsToStandardWhenTheEntryPointSaysNothing() {
        var model = EntryPoint();

        Assert.Equal(ResponseModelValue.Standard, ResponseModelSelector.Read(model));
    }

    [Fact]
    public void Read_DefaultsToStandardWhenThereAreNoAttributesAtAll() {
        var model = EntryPoint();
        model.AttributeModels = null!;

        Assert.Equal(ResponseModelValue.Standard, ResponseModelSelector.Read(model));
    }

    [Fact]
    public void Read_IgnoresOtherModuleAttributes() {
        var model = EntryPoint(Attribute("CaseInsensitiveRoutesAttribute", ""));

        Assert.Equal(ResponseModelValue.Standard, ResponseModelSelector.Read(model));
    }

    #endregion

    #region the spellings a user can write

    /// <summary>
    /// The arguments arrive with names qualified by the rewriter, so this is the form the generator
    /// sees most of the time.
    /// </summary>
    [Fact]
    public void Read_UnderstandsTheFullyQualifiedMember() {
        var model = EntryPoint(Attribute(
            "ResponseModelAttribute", "Hardened.Requests.Abstract.Responses.ResponseModel.Union"));

        Assert.Equal(ResponseModelValue.Union, ResponseModelSelector.Read(model));
    }

    [Fact]
    public void Read_UnderstandsTheEnumQualifiedMember() {
        var model = EntryPoint(Attribute("ResponseModelAttribute", "ResponseModel.Response"));

        Assert.Equal(ResponseModelValue.Response, ResponseModelSelector.Read(model));
    }

    /// <summary>
    /// Legal C# where the parameter type makes the enum unambiguous, and a spelling a generator
    /// that only handled the qualified form would silently read as Standard.
    /// </summary>
    [Fact]
    public void Read_UnderstandsTheBareMember() {
        var model = EntryPoint(Attribute("ResponseModelAttribute", "Union"));

        Assert.Equal(ResponseModelValue.Union, ResponseModelSelector.Read(model));
    }

    /// <summary>
    /// Both spellings of the attribute name are the same attribute, and a generator recognising
    /// only one would do nothing for a project that wrote the other.
    /// </summary>
    [Fact]
    public void Read_AcceptsTheNameWithAndWithoutTheAttributeSuffix() {
        Assert.Equal(
            ResponseModelValue.Union,
            ResponseModelSelector.Read(EntryPoint(Attribute("ResponseModel", "ResponseModel.Union"))));

        Assert.Equal(
            ResponseModelValue.Union,
            ResponseModelSelector.Read(
                EntryPoint(Attribute("ResponseModelAttribute", "ResponseModel.Union"))));
    }

    [Fact]
    public void Read_ReadsStandardAsStandard() {
        var model = EntryPoint(Attribute("ResponseModelAttribute", "ResponseModel.Standard"));

        Assert.Equal(ResponseModelValue.Standard, ResponseModelSelector.Read(model));
    }

    /// <summary>
    /// A value this generator does not know means the attribute was written against a newer
    /// Hardened. Standard is the answer that still builds.
    /// </summary>
    [Fact]
    public void Read_FallsBackToStandardForAnUnknownMember() {
        var model = EntryPoint(Attribute("ResponseModelAttribute", "ResponseModel.Whatever"));

        Assert.Equal(ResponseModelValue.Standard, ResponseModelSelector.Read(model));
    }

    #endregion

    #region declared versus defaulted

    /// <summary>
    /// Saying Standard and saying nothing produce the same emit and are not the same statement.
    /// </summary>
    [Fact]
    public void IsDeclared_SeparatesAnExplicitStandardFromSilence() {
        Assert.False(ResponseModelSelector.IsDeclared(EntryPoint()));

        Assert.True(ResponseModelSelector.IsDeclared(
            EntryPoint(Attribute("ResponseModelAttribute", "ResponseModel.Standard"))));
    }

    #endregion

    private static EntryPointSelector.Model EntryPoint(params AttributeModel[] attributes) =>
        new() {
            EntryPointType = TypeDefinition.Get("MyApp", "Application"),
            AttributeModels = attributes
        };

    private static AttributeModel Attribute(string name, string arguments) =>
        new(TypeDefinition.Get("MyApp", name), arguments, "");
}
