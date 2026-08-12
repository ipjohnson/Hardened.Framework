using System.Reflection;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.CacheControl;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Attributes;

/// <summary>
/// The route attributes as a consumer meets them: applied to a method in another assembly and
/// read back.
///
/// <para>
/// <c>DeleteAttribute</c> and <c>PatchAttribute</c> shipped from the first commit (2022-07-02) as
/// empty <c>internal</c> classes that did not derive from <see cref="Attribute"/>. No project
/// could apply either one, while the generator's verb list, the runtime, the README and the
/// package description all advertised both. Nobody noticed for three years because nothing
/// asserted the shipped surface from a consumer's position. Fixed 2026-08-11.
/// </para>
///
/// <para>
/// The controllers below are ordinary classes in a referencing assembly, which is exactly the
/// position that failed. <c>HttpMethodTests</c> covers the same verbs end to end through a real
/// application; this covers the narrower claim that the attribute types themselves are usable.
/// </para>
/// </summary>
public class RouteAttributeTests {

    private class VerbController {
        [Get("/get")]
        public string Get() => "get";

        [Post("/post")]
        public string Post() => "post";

        [Put("/put")]
        public string Put() => "put";

        [Delete("/delete")]
        public string Delete() => "delete";

        [Patch("/patch")]
        public string Patch() => "patch";

        [Get]
        public string NoPath() => "no-path";
    }

    [BasePath("/api")]
    private class BasePathController { }

    private class BindingController {
        public string Named(
            [FromHeader("X-Tenant")] string tenant,
            [FromQueryString("q")] string term) => tenant + term;

        public string Unnamed(
            [FromHeader] string tenant,
            [FromQueryString] string term) => tenant + term;
    }

    [Theory]
    [InlineData(typeof(GetAttribute))]
    [InlineData(typeof(PostAttribute))]
    [InlineData(typeof(PutAttribute))]
    [InlineData(typeof(DeleteAttribute))]
    [InlineData(typeof(PatchAttribute))]
    public void EveryVerbAttributeIsPublicAndDerivesFromAttribute(Type attributeType) {
        Assert.True(attributeType.IsPublic, $"{attributeType.Name} is not public");
        Assert.True(attributeType.IsSubclassOf(typeof(Attribute)), $"{attributeType.Name} is not an Attribute");
    }

    [Theory]
    [InlineData("Get", typeof(GetAttribute), "/get")]
    [InlineData("Post", typeof(PostAttribute), "/post")]
    [InlineData("Put", typeof(PutAttribute), "/put")]
    [InlineData("Delete", typeof(DeleteAttribute), "/delete")]
    [InlineData("Patch", typeof(PatchAttribute), "/patch")]
    public void EveryVerbAttributeCanBeAppliedAndItsPathReadBack(
        string methodName, Type attributeType, string expectedPath) {
        var method = typeof(VerbController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public)!;

        var attribute = method.GetCustomAttributes(attributeType, inherit: false).Single();

        Assert.Equal(expectedPath, PathOf(attribute));
    }

    /// <summary>
    /// The path argument is optional on every verb. The generator turns the absent argument into
    /// <c>"/"</c>; the attribute's own default is the empty string, so a consumer reading the
    /// attribute back sees that rather than the route the handler is reachable at.
    /// </summary>
    [Fact]
    public void AVerbAttributeAppliedWithNoArgumentCarriesAnEmptyPath() {
        var method = typeof(VerbController).GetMethod("NoPath", BindingFlags.Instance | BindingFlags.Public)!;

        Assert.Equal("", PathOf(method.GetCustomAttribute<GetAttribute>()!));
    }

    /// <summary>
    /// The four status properties <c>[Get]</c> and the rest declared until 2026-08-11 are gone.
    /// Sixteen of them across four attributes compiled and did nothing: the web generator's
    /// <c>RequestHandlerNameModel</c> carries only path and method, so no value ever reached the
    /// emitted handler info. Only <c>NullReturnStatus</c> had an interface slot anything read, and
    /// even its declared defaults contradicted the runtime. See docs/TESTING-PLAN.md §2.3.
    /// </summary>
    [Theory]
    [InlineData(typeof(GetAttribute))]
    [InlineData(typeof(PostAttribute))]
    [InlineData(typeof(PutAttribute))]
    [InlineData(typeof(DeleteAttribute))]
    [InlineData(typeof(PatchAttribute))]
    public void NoVerbAttributeDeclaresAStatusPropertyNothingReads(Type attributeType) {
        var declared = attributeType.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["Path"], declared);
    }

    [Fact]
    public void BasePathCanBeAppliedToAClassAndItsPathReadBack() {
        Assert.Equal("/api", typeof(BasePathController).GetCustomAttribute<BasePathAttribute>()!.Path);
    }

    /// <summary>
    /// <c>[BasePath]</c> is declared for classes and assemblies, and the web generator reads it in
    /// both positions — on a controller and on the <c>[HardenedModule]</c> entry point.
    /// </summary>
    [Fact]
    public void BasePathIsDeclaredForClassesAndAssemblies() {
        var usage = typeof(BasePathAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Assembly, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    /// <summary>
    /// <c>[CacheControl]</c>'s defaults are what a handler that names only a max age is served
    /// with, so they are part of the contract rather than an implementation detail.
    /// </summary>
    [Fact]
    public void CacheControlDefaultsToAPublicMaxAgeOfZero() {
        var attribute = new CacheControlAttribute();

        Assert.Equal(0, attribute.MaxAge);
        Assert.Equal(CacheControlEnum.MaxAge | CacheControlEnum.Public, attribute.Type);
    }

    [Fact]
    public void CacheControlValuesSurviveBeingSet() {
        var attribute = new CacheControlAttribute { MaxAge = 86400, Type = CacheControlEnum.NoStore };

        Assert.Equal(86400, attribute.MaxAge);
        Assert.Equal(CacheControlEnum.NoStore, attribute.Type);
    }

    /// <summary>
    /// The two binding attributes the web package adds on top of
    /// <c>Hardened.Requests.Abstract</c>'s. Both are applied to a parameter and read back, because
    /// the named form is what emitted a double-quoted string literal before the 2026-08-11
    /// generator fix — code that could not compile in any project using it.
    /// </summary>
    [Fact]
    public void TheWebBindingAttributesCarryTheNameTheyWereGiven() {
        var parameters = typeof(BindingController)
            .GetMethod("Named", BindingFlags.Instance | BindingFlags.Public)!
            .GetParameters();

        Assert.Equal("X-Tenant", parameters[0].GetCustomAttribute<FromHeaderAttribute>()!.Name);
        Assert.Equal("q", parameters[1].GetCustomAttribute<FromQueryStringAttribute>()!.Name);
    }

    /// <summary>
    /// The name is optional on both. An unnamed binding falls back to the parameter's own name,
    /// which the generator supplies — the attribute itself carries null.
    /// </summary>
    [Fact]
    public void AnUnnamedWebBindingAttributeCarriesNoName() {
        var parameters = typeof(BindingController)
            .GetMethod("Unnamed", BindingFlags.Instance | BindingFlags.Public)!
            .GetParameters();

        Assert.Null(parameters[0].GetCustomAttribute<FromHeaderAttribute>()!.Name);
        Assert.Null(parameters[1].GetCustomAttribute<FromQueryStringAttribute>()!.Name);
    }

    private static string PathOf(object attribute) =>
        (string)attribute.GetType().GetProperty("Path")!.GetValue(attribute)!;
}
