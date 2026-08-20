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

    private static class ConstraintController {
        [RouteConstraint("isbn")]
        public static bool IsIsbn(ReadOnlySpan<char> value) => value.Length == 13;
    }

    [Server("https://api.example.com", "Production")]
    [Server("https://staging.example.com")]
    private class ServedApplication { }

    [Tag("Products")]
    private class V2ProductsController { }

    [CaseInsensitiveRoutes]
    private class LenientApplication { }

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
    /// Every verb declares <c>SuccessStatus</c>, and nothing else beyond its path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The four status properties these carried until 2026-08-11 were removed because nothing read
    /// them - sixteen across four attributes that compiled and did nothing, with declared defaults
    /// that contradicted the runtime.
    /// </para>
    /// <para>
    /// <c>SuccessStatus</c> is back and wired, because "nothing reads it" was an argument for
    /// reading it. A described operation states its status through the document; without this a
    /// hand-written handler would be the only kind that could not say the same thing. Both reach
    /// <c>ResponseInformationModel.DefaultStatusCode</c> and one runtime behaviour.
    /// </para>
    /// <para>
    /// The other three stay gone. A validation or error status asserted by a hand-written attribute
    /// has no source of truth behind it, which is what made the original four dead weight.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(typeof(GetAttribute))]
    [InlineData(typeof(PostAttribute))]
    [InlineData(typeof(PutAttribute))]
    [InlineData(typeof(DeleteAttribute))]
    [InlineData(typeof(PatchAttribute))]
    public void EveryVerbDeclaresSuccessStatusAndNoOtherStatus(Type attributeType) {
        var declared = attributeType.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Path", "SuccessStatus"], declared);
    }

    /// <summary>
    /// Unset means 200, and 0 is what unset looks like through reflection.
    /// </summary>
    /// <remarks>
    /// Deliberately not defaulted to 200. The generator treats 200 and unset alike, and a default of
    /// 0 makes "the author said nothing" distinguishable from "the author asked for 200" if that
    /// ever needs telling apart. <c>NullReturnStatus = 404</c> shipping on <c>[Delete]</c> while the
    /// runtime answered 200 is what a wrong default looks like.
    /// </remarks>
    [Theory]
    [InlineData(typeof(GetAttribute))]
    [InlineData(typeof(PostAttribute))]
    [InlineData(typeof(PutAttribute))]
    [InlineData(typeof(DeleteAttribute))]
    [InlineData(typeof(PatchAttribute))]
    public void SuccessStatusDefaultsToUnset(Type attributeType) {
        var attribute = Activator.CreateInstance(attributeType, "")!;

        Assert.Equal(0, attribute.GetType().GetProperty("SuccessStatus")!.GetValue(attribute));
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

    /// <summary>
    /// <c>[RouteConstraint]</c> names a constraint a route template may then use after a colon.
    /// The name is the whole contract between the two halves — the generator matches
    /// <c>{code:isbn}</c> against it by string — so a consumer reading it back has to see what it
    /// wrote.
    /// </summary>
    [Fact]
    public void RouteConstraintCarriesTheNameARouteTemplateUses() {
        var method = typeof(ConstraintController).GetMethod("IsIsbn", BindingFlags.Static | BindingFlags.Public)!;

        Assert.Equal("isbn", method.GetCustomAttribute<RouteConstraintAttribute>()!.Name);
    }

    /// <summary>
    /// Declared for methods and repeatable, because one static method can serve as more than one
    /// named constraint and a class may declare several.
    /// </summary>
    [Fact]
    public void RouteConstraintIsDeclaredForMethodsAndRepeats() {
        var usage = typeof(RouteConstraintAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    /// <summary>
    /// <c>[Server]</c> is the one part of a generated document that cannot be derived from the
    /// code, so both halves have to survive being read back — a URL with no description is the
    /// common form, and the description is what tells two servers apart.
    /// </summary>
    [Fact]
    public void ServerCarriesItsUrlAndOptionalDescription() {
        var servers = typeof(ServedApplication)
            .GetCustomAttributes<ServerAttribute>(inherit: false)
            .OrderBy(server => server.Url)
            .ToArray();

        Assert.Equal("https://api.example.com", servers[0].Url);
        Assert.Equal("Production", servers[0].Description);

        Assert.Equal("https://staging.example.com", servers[1].Url);
        Assert.Null(servers[1].Description);
    }

    /// <summary>
    /// Repeatable and declared for classes and assemblies, because an application served from
    /// several places names each one, and the entry point is where it goes.
    /// </summary>
    [Fact]
    public void ServerIsDeclaredForClassesAndAssembliesAndRepeats() {
        var usage = typeof(ServerAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class | AttributeTargets.Assembly, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
    }

    /// <summary>
    /// <c>[Tag]</c> overrides the group a controller's operations document under. Tags are what a
    /// specification-first build turns back into service interfaces, so a name that did not survive
    /// the round trip would collapse the controller structure rather than merely mislabel it.
    /// </summary>
    [Fact]
    public void TagCarriesTheGroupNameItWasGiven() {
        Assert.Equal("Products", typeof(V2ProductsController).GetCustomAttribute<TagAttribute>()!.Name);
    }

    /// <summary>
    /// One tag per controller — the controller already is the group, so a second one would be
    /// asking the document to put the same operations in two places.
    /// </summary>
    [Fact]
    public void TagIsDeclaredForOneClassAtATime() {
        var usage = typeof(TagAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
    }

    /// <summary>
    /// <c>[CaseInsensitiveRoutes]</c> is a marker — it carries no state, and its presence on the
    /// entry point is the whole signal. It is applied here from a referencing assembly for the same
    /// reason the verb attributes are: a marker nothing can apply is a marker that does nothing.
    /// </summary>
    [Fact]
    public void CaseInsensitiveRoutesIsAMarkerAConsumerCanApply() {
        Assert.NotNull(typeof(LenientApplication).GetCustomAttribute<CaseInsensitiveRoutesAttribute>());

        Assert.Empty(typeof(CaseInsensitiveRoutesAttribute).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
    }

    private static string PathOf(object attribute) =>
        (string)attribute.GetType().GetProperty("Path")!.GetValue(attribute)!;
}
