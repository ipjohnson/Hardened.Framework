using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Caching;

/// <summary>
/// Incremental generator caching is built on model equality. Roslyn keeps a cached result
/// and reuses it only when the model produced by the new run compares equal to the previous
/// one. Any model that falls back to reference equality is never equal to its successor, so
/// the pipeline re-runs on every keystroke - output stays correct, nothing fails, the IDE
/// just gets slower for no visible reason.
///
/// These assert the property caching actually depends on: a model rebuilt from identical
/// inputs equals the original, and differing inputs do not.
/// </summary>
public class ModelEqualityCachingTests {

    private static ITypeDefinition Type(string ns, string name) => TypeDefinition.Get(ns, name);

    private static RequestHandlerModel Model(
        string path = "/orders/{id}",
        string method = "GET",
        string handlerMethod = "GetOrder",
        string returnTypeName = "String") =>
        new(
            new RequestHandlerNameModel(path, method),
            Type("TestApp", "OrderController"),
            handlerMethod,
            Type("TestApp", "OrderController_GetOrder"),
            new List<RequestParameterInformation> {
                new(
                    parameterType: Type("System", "String"),
                    name: "id",
                    required: true,
                    defaultValue: null,
                    bindingType: ParameterBindType.Path,
                    bindingName: "id",
                    parameterIndex: 0)
            },
            new ResponseInformationModel { ReturnType = Type("System", returnTypeName) },
            new List<AttributeModel> {
                new(Type("TestApp", "AuthorizeAttribute"), "", "")
            });

    /// <summary>
    /// Everything else rests on this. ITypeDefinition comes from CSharpAuthor and appears in
    /// nearly every model; if it compared by reference, no model built on it could ever
    /// cache.
    /// </summary>
    [Fact]
    public void TypeDefinitionComparesStructurally() {
        Assert.Equal(Type("System", "String"), Type("System", "String"));
        Assert.Equal(Type("System", "String").GetHashCode(), Type("System", "String").GetHashCode());
        Assert.NotEqual(Type("System", "String"), Type("System", "Int32"));
    }

    [Fact]
    public void IdenticallyBuiltModelsAreEqual() {
        Assert.True(Model().Equals(Model()),
            "two models built from identical inputs must compare equal or the generator " +
            "pipeline re-runs on every edit");
    }

    [Fact]
    public void IdenticallyBuiltModelsShareAHashCode() {
        Assert.Equal(Model().GetHashCode(), Model().GetHashCode());
    }

    [Theory]
    [InlineData("/orders/{id}", "POST", "GetOrder", "String")]
    [InlineData("/other/{id}", "GET", "GetOrder", "String")]
    [InlineData("/orders/{id}", "GET", "DeleteOrder", "String")]
    [InlineData("/orders/{id}", "GET", "GetOrder", "Int32")]
    public void ModelsDifferingInAnyMemberAreNotEqual(
        string path, string method, string handlerMethod, string returnTypeName) {
        Assert.False(Model().Equals(Model(path, method, handlerMethod, returnTypeName)),
            "a model that differs must not compare equal, or a real code change would be " +
            "cached away and never regenerated");
    }

    [Fact]
    public void ModelIsNotEqualToAnUnrelatedObject() {
        Assert.False(Model().Equals("not a model"));
        Assert.False(Model().Equals(null!));
    }

    [Fact]
    public void ComparerAgreesWithTheModel() {
        var comparer = new RequestHandlerModelComparer();

        Assert.True(comparer.Equals(Model(), Model()));
        Assert.Equal(comparer.GetHashCode(Model()), comparer.GetHashCode(Model()));
        Assert.False(comparer.Equals(Model(), Model(method: "DELETE")));
    }

    [Fact]
    public void RequestHandlerNameModelComparesStructurally() {
        var a = new RequestHandlerNameModel("/a", "GET");
        var b = new RequestHandlerNameModel("/a", "GET");

        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(new RequestHandlerNameModel("/a", "POST")));
        Assert.False(a.Equals(new RequestHandlerNameModel("/b", "GET")));
    }

    /// <summary>
    /// ResponseInformationModel and AttributeModel are records, so equality is generated
    /// member-wise. That is only structural while their members are - a collection member
    /// would silently revert them to reference equality.
    /// </summary>
    [Fact]
    public void RecordModelsCompareStructurally() {
        var responseA = new ResponseInformationModel { ReturnType = Type("System", "String"), IsAsync = true };
        var responseB = new ResponseInformationModel { ReturnType = Type("System", "String"), IsAsync = true };

        Assert.Equal(responseA, responseB);
        Assert.Equal(responseA.GetHashCode(), responseB.GetHashCode());
        Assert.NotEqual(responseA, new ResponseInformationModel { ReturnType = Type("System", "String") });

        var attributeA = new AttributeModel(Type("TestApp", "AuthorizeAttribute"), "args", "props");
        var attributeB = new AttributeModel(Type("TestApp", "AuthorizeAttribute"), "args", "props");

        Assert.Equal(attributeA, attributeB);
        Assert.Equal(attributeA.GetHashCode(), attributeB.GetHashCode());
        Assert.NotEqual(attributeA, attributeA with { Arguments = "different" });
    }
}
