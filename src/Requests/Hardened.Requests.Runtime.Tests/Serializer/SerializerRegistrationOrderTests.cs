using System.Text.Json.Serialization.Metadata;
using Hardened.Requests.Abstract.Serializer;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Requests.Runtime.Serializer;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hardened.Requests.Runtime.Tests.Serializer;

/// <summary>
/// Which serializer answers for a media type two of them claim.
/// </summary>
/// <remarks>
/// <para>
/// <b>The last registration wins, and that is the whole of response-side precedence.</b> There used
/// to be an <c>Order</c> as well, because reverse-registration order within a module is decided by
/// how implementation type names sort and an application could not steer it. A declared content type
/// makes the contest a lookup, and this rule decides it.
/// </para>
/// <para>
/// What makes it steerable is that DependencyModules applies dependencies before dependents and the
/// root application last, so a module the application imports lands after the framework module it
/// depends on. That is why importing <c>[AotSerializerModule]</c>, or the Newtonsoft package, is
/// enough to be sure the serializer it brings is the one that answers.
/// </para>
/// </remarks>
public class SerializerRegistrationOrderTests {

    private static IOptions<IJsonSerializerConfiguration> JsonConfiguration() =>
        Options.Create<IJsonSerializerConfiguration>(new JsonSerializerConfiguration());

    private static IResponseSerializer Aot() =>
        new AotResponseSerializer(JsonConfiguration(), Array.Empty<IJsonTypeInfoResolver>());

    private static IResponseSerializer SystemTextJson() =>
        new SystemTextJsonResponseSerializer(JsonConfiguration(), Array.Empty<IJsonTypeInfoResolver>());

    private static SerializationLocatorService Locator(params IResponseSerializer[] serializers) =>
        new(Array.Empty<IRequestDeserializer>(), serializers);

    /// <summary>
    /// Both JSON serializers declare <c>application/json</c>, which is what puts them in one
    /// contest at all.
    /// </summary>
    [Fact]
    public void BothJsonSerializersDeclareTheSameMediaType() {
        Assert.Equal("application/json", Aot().ContentType);
        Assert.Equal(Aot().ContentType, SystemTextJson().ContentType);
    }

    /// <summary>
    /// The AOT serializer answers when it registered last, which is what importing
    /// <c>[AotSerializerModule]</c> produces.
    /// </summary>
    [Fact]
    public void TheLastRegistrationUnderAMediaTypeAnswers() {
        Assert.IsType<AotResponseSerializer>(
            Locator(SystemTextJson(), Aot()).ProducerOf("application/json"));
    }

    /// <summary>
    /// And the other way round, because the point is that the answer follows registration rather
    /// than anything either class declares about itself.
    /// </summary>
    [Fact]
    public void TheOtherOrderResolvesTheOtherWay() {
        Assert.IsType<SystemTextJsonResponseSerializer>(
            Locator(Aot(), SystemTextJson()).ProducerOf("application/json"));
    }

    /// <summary>
    /// A media type nothing declares has no producer, which is what the build warning and
    /// <c>ContentTypeNotProducibleException</c> are both about.
    /// </summary>
    [Fact]
    public void AMediaTypeNothingDeclaresHasNoProducer() {
        Assert.Null(Locator(SystemTextJson()).ProducerOf("application/vnd.msgpack"));
    }

    /// <summary>Matched without regard to case, as media types are compared everywhere else.</summary>
    [Fact]
    public void TheLookupIsCaseInsensitive() {
        Assert.NotNull(Locator(SystemTextJson()).ProducerOf("APPLICATION/JSON"));
    }

    /// <summary>
    /// The default is what an operation declaring nothing is bound to, so it has to be found the
    /// same way - last registration first.
    /// </summary>
    [Fact]
    public void TheDefaultSerializerIsTheLastRegisteredDefault() {
        Assert.IsType<AotResponseSerializer>(Locator(SystemTextJson(), Aot()).DefaultSerializer);
    }

    /// <summary>
    /// <c>RawResponseSerializer</c> is never the default, whenever it registered. A response value
    /// that is not already bytes is not its business.
    /// </summary>
    [Fact]
    public void TheRawWriterIsNeverTheDefault() {
        Assert.IsType<SystemTextJsonResponseSerializer>(
            Locator(SystemTextJson(), new RawResponseSerializer()).DefaultSerializer);
    }

    /// <summary>A container with no serializers in it has no default, rather than throwing.</summary>
    [Fact]
    public void AnEmptyContainerHasNoDefault() {
        Assert.Null(Locator().DefaultSerializer);
    }
}
