using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

public class RequestParameterInformationTests {

    private static RequestParameterInformation Body(bool requiresServices = false) =>
        new(TypeDefinition.Get("TestApp", "EventStore"),
            "store", true, null, ParameterBindType.Body, "store", 0,
            constructorRequiresServices: requiresServices);

    /// <summary>
    /// <c>ConstructorRequiresServices</c> rides on the model, so an edit to the type's constructor
    /// has to break the model's equality. Left out, the cached model keeps the old answer and
    /// HRDR007 comes and goes with whatever else forced a regeneration.
    /// </summary>
    [Fact]
    public void AServiceShapedBodyDoesNotCompareEqualToAnOrdinaryOne() {
        var body = Body();
        var service = new RequestParameterInformation(
            TypeDefinition.Get("TestApp", "EventStore"),
            "store", true, null, ParameterBindType.Body, "store", 0,
            constructorRequiresServices: true);

        Assert.NotEqual(body, service);
        Assert.NotEqual(body.GetHashCode(), service.GetHashCode());
    }

    /// <summary>
    /// Reordering rebuilds every parameter, so the copy has to carry everything the original held.
    /// It did not, and the parameter that reached the handler stage said its type was an ordinary
    /// body - which turned HRDR007 off for exactly the handlers it exists to report.
    /// </summary>
    [Fact]
    public void ReindexingCarriesEveryProperty() {
        var original = new RequestParameterInformation(
            TypeDefinition.Get("TestApp", "EventStore"),
            "store", true, "5", ParameterBindType.QueryString, "wire", 0,
            constructorRequiresServices: true) {
            Description = "prose"
        };

        var moved = original.WithIndex(3);

        Assert.Equal(3, moved.ParameterIndex);
        Assert.Equal(original.ParameterType, moved.ParameterType);
        Assert.Equal(original.Name, moved.Name);
        Assert.Equal(original.Required, moved.Required);
        Assert.Equal(original.DefaultValue, moved.DefaultValue);
        Assert.Equal(original.BindingType, moved.BindingType);
        Assert.Equal(original.BindingName, moved.BindingName);
        Assert.Equal(original.CustomAttribute, moved.CustomAttribute);
        Assert.Equal(original.Description, moved.Description);
        Assert.True(moved.ConstructorRequiresServices);
    }

    [Fact]
    public void TwoParametersAgreeingOnItCompareEqual() {
        var left = Body(true);
        var right = Body(true);

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }
}
