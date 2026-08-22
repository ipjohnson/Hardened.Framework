using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Hardened.Requests.Runtime.Tests.Support;
using Hardened.Shared.Runtime.Json;
using IRequestsJsonConfiguration = Hardened.Requests.Runtime.Configuration.IJsonSerializerConfiguration;
using RequestsJsonConfiguration = Hardened.Requests.Runtime.Configuration.JsonSerializerConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// A registered <c>IJsonTypeInfoResolver</c> decides how a type is written, on every host.
/// </summary>
/// <remarks>
/// <para>
/// <c>services.AddSingleton&lt;IJsonTypeInfoResolver&gt;(MyContext.Default)</c> is the AOT-safe way
/// to say how an enum reaches the wire, and it did not work on a JIT host in either direction. The
/// reflection serializers did not take resolvers at all, so the registration was accepted and
/// dropped. The AOT serializers took them and appended them behind a
/// <c>DefaultJsonTypeInfoResolver</c>, which answers for nearly every type, so they were never
/// reached.
/// </para>
/// <para>
/// Neither failed. Both wrote <c>{"category":0}</c> from a context that says enums are strings,
/// which is a defect no exception marks and no build reports - and it inverted where it matters,
/// because a published AOT application installs no reflection resolver and therefore behaved
/// correctly while the tests covering it did not.
/// </para>
/// </remarks>
public class ResolverPrecedenceTests {

    // Aliased because two public types share this name - see D10. Hardened.Shared.Runtime.Json
    // has an IJsonSerializerConfiguration too, and importing both namespaces is enough to stop the
    // file compiling.
    private static IOptions<IRequestsJsonConfiguration> Config() =>
        Options.Create<IRequestsJsonConfiguration>(new RequestsJsonConfiguration());

    private static IExecutionContext ResponseContext(object value, out MemoryStream body) {
        var context = Substitute.For<IExecutionContext>();
        var request = Substitute.For<IExecutionRequest>();
        var response = Substitute.For<IExecutionResponse>();

        body = new MemoryStream();
        request.Accept.Returns("application/json");
        response.Body.Returns(body);
        response.ResponseValue.Returns(value);
        response.ShouldCompress.Returns(false);
        context.Request.Returns(request);
        context.Response.Returns(response);

        return context;
    }

    private static IExecutionContext RequestContext(string json) {
        var context = Pipeline.Context(method: "POST", body: Encoding.UTF8.GetBytes(json));

        context.Request.Headers[KnownHeaders.ContentType] = new StringValues("application/json");

        return context;
    }

    public static TheoryData<string> ResponseSerializers => new() {
        nameof(SystemTextJsonResponseSerializer),
        nameof(AotResponseSerializer),
        nameof(StreamingJsonResponseSerializer)
    };

    private static IResponseSerializer ResponseSerializerNamed(
        string name, params IJsonTypeInfoResolver[] resolvers) => name switch {
        nameof(SystemTextJsonResponseSerializer) =>
            new SystemTextJsonResponseSerializer(Config(), resolvers),
        nameof(AotResponseSerializer) =>
            new AotResponseSerializer(Config(), resolvers),
        nameof(StreamingJsonResponseSerializer) =>
            new StreamingJsonResponseSerializer(Config(), resolvers),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown serializer")
    };

    /// <summary>
    /// The D10 case: an enum reaches the wire as a name rather than as its ordinal.
    /// </summary>
    /// <remarks>
    /// The member name, not a camel-cased form of it. <c>PropertyNamingPolicy</c> governs property
    /// names and does not reach enum members, so <c>Web</c> defaults produce a camelCase property
    /// holding a PascalCase value - <c>{"category":"ScienceFiction"}</c>. Asserted exactly, because
    /// it is the wire format: an application wanting <c>science-fiction</c> supplies a converter
    /// carrying that vocabulary, and changing this later breaks every consumer.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ResponseSerializers))]
    public async Task SerializeResponse_HonoursTheRegisteredContextForAnEnum(string serializerName) {
        var serializer = ResponseSerializerNamed(serializerName, CatalogContext.Default);
        var context = ResponseContext(new Listing(Category.ScienceFiction), out var body);

        await serializer.SerializeResponse(context);

        var json = Encoding.UTF8.GetString(body.ToArray());

        Assert.Equal("{\"category\":\"ScienceFiction\"}", json);
        Assert.DoesNotContain("\"category\":0", json);
    }

    /// <summary>
    /// Reading has to agree with writing, or the application answers 400 to its own output — which
    /// is how this surfaced, as a test host posting a body the application under test refused.
    /// </summary>
    [Fact]
    public async Task DeserializeRequestBody_HonoursTheRegisteredContextForAnEnum() {
        var deserializer = new SystemTextJsonRequestDeserializer(
            Config(),
            NullLogger<SystemTextJsonRequestDeserializer>.Instance,
            new IJsonTypeInfoResolver[] { CatalogContext.Default });

        var listing = await deserializer.DeserializeRequestBody<Listing>(
            RequestContext("""{"category":"ScienceFiction"}"""));

        Assert.Equal(Category.ScienceFiction, listing!.Category);
    }

    /// <summary>
    /// Reflection still covers what no context declares. A context knows the models the application
    /// wrote and not much else, so putting it first must not amount to withholding the fallback.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResponseSerializers))]
    public async Task SerializeResponse_StillReflectsATypeNoContextDeclares(string serializerName) {
        var serializer = ResponseSerializerNamed(serializerName, CatalogContext.Default);
        var context = ResponseContext(new Undeclared("only reflection knows this"), out var body);

        await serializer.SerializeResponse(context);

        Assert.Contains("only reflection knows this", Encoding.UTF8.GetString(body.ToArray()));
    }

    /// <summary>
    /// No resolver registered is still the common case, and still resolves by reflection.
    /// </summary>
    [Theory]
    [MemberData(nameof(ResponseSerializers))]
    public async Task SerializeResponse_FallsBackToReflectionWhenNothingIsRegistered(string serializerName) {
        var serializer = ResponseSerializerNamed(serializerName);
        var context = ResponseContext(new Undeclared("reflection"), out var body);

        await serializer.SerializeResponse(context);

        Assert.Contains("reflection", Encoding.UTF8.GetString(body.ToArray()));
    }

    /// <summary>
    /// The ordering rule itself, stated once where the serializers get it from.
    /// </summary>
    [Fact]
    public void WithResolvers_PutsRegisteredResolversAheadOfReflection() {
        var options = JsonTypeInfoLookup.WithResolvers(
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            new IJsonTypeInfoResolver[] { CatalogContext.Default });

        var chain = options.TypeInfoResolverChain;

        Assert.Same(CatalogContext.Default, chain[0]);
        Assert.IsType<DefaultJsonTypeInfoResolver>(chain[^1]);
    }

    /// <summary>
    /// <c>WithReflectionFallback</c> installs reflection only onto an empty chain — any resolver at
    /// all makes <c>TypeInfoResolver</c> non-null and silences it. That is the guard the serializers
    /// used to build on, and this pins the difference so the two are not swapped back.
    /// </summary>
    [Fact]
    public void AppendReflectionFallback_AddsReflectionEvenWhenTheChainIsNotEmpty() {
        var withGuard = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        withGuard.TypeInfoResolverChain.Add(CatalogContext.Default);
        JsonTypeInfoLookup.WithReflectionFallback(withGuard);

        Assert.DoesNotContain(withGuard.TypeInfoResolverChain, r => r is DefaultJsonTypeInfoResolver);

        var appended = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        appended.TypeInfoResolverChain.Add(CatalogContext.Default);
        JsonTypeInfoLookup.AppendReflectionFallback(appended);

        Assert.Contains(appended.TypeInfoResolverChain, r => r is DefaultJsonTypeInfoResolver);
    }

    [Fact]
    public void AppendReflectionFallback_DoesNotAddASecondReflectionResolver() {
        var options = JsonTypeInfoLookup.AppendReflectionFallback(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        JsonTypeInfoLookup.AppendReflectionFallback(options);

        Assert.Single(options.TypeInfoResolverChain, r => r is DefaultJsonTypeInfoResolver);
    }
}

internal enum Category { ScienceFiction, Tools }

internal record Listing(Category Category);

internal record Undeclared(string Name);

/// <summary>
/// What an application writes to say how its own enums are written, without an attribute on the
/// enum and without a converter that Native AOT cannot build.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Listing))]
internal partial class CatalogContext : JsonSerializerContext { }
