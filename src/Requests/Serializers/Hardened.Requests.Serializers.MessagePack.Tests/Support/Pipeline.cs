using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Serializers.MessagePack.Impl;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Collections;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Hardened.Requests.Serializers.MessagePack.Tests.Support;

/// <summary>
/// Real request, real response, real pool - the arrangement
/// <c>Hardened.Requests.Serializers.Newtonsoft.Tests</c> uses, and for its reason: both halves here
/// borrow a pooled stream, and a substitute handing back a fresh one every call cannot fail when
/// that ownership is broken.
/// </summary>
public static class Pipeline {

    public static MemoryStreamPool Pool() => new();

    /// <summary>
    /// The options the package composes, built through <see cref="SharedMessagePackOptions"/> rather
    /// than by hand, so a test exercises the resolver chain the package actually installs.
    /// </summary>
    public static ISharedMessagePackOptions Options(
        Func<IServiceProvider, MessagePackSerializerOptions>? provider = null,
        params IFormatterResolver[] resolvers) {
        var configuration = new MessagePackSerializerConfiguration();

        if (provider != null) {
            configuration.OptionsProvider = provider;
        }

        var services = new ServiceCollection().BuildServiceProvider();

        return new SharedMessagePackOptions(
            services,
            Microsoft.Extensions.Options.Options.Create<IMessagePackSerializerConfiguration>(configuration),
            resolvers);
    }

    public static MessagePackRequestDeserializer Deserializer(
        IMemoryStreamPool pool, ISharedMessagePackOptions? options = null) =>
        new(options ?? Options(), pool);

    public static MessagePackResponseSerializer ResponseSerializer(
        IMemoryStreamPool pool, ISharedMessagePackOptions? options = null) =>
        new(options ?? Options(), pool);

    /// <summary>A context whose request body is <paramref name="body"/>.</summary>
    public static IExecutionContext Context(
        byte[]? body = null, string? contentType = MessagePackContentType.Value) {
        var provider = new ServiceCollection().BuildServiceProvider();

        var request = new TestExecutionRequest(
            "POST", "/", contentType ?? MessagePackContentType.Value,
            new SimpleQueryStringCollection(new Dictionary<string, string>())) {
            Body = body is null ? Stream.Null : new MemoryStream(body)
        };

        if (contentType != null) {
            request.Headers[KnownHeaders.ContentType] = contentType;
        }

        return new TestExecutionContext(
            provider,
            provider,
            Substitute.For<IKnownServices>(),
            request,
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None,
            null);
    }

    /// <summary>Everything written to the response body.</summary>
    public static byte[] BodyOf(IExecutionContext context) {
        context.Response.Body.Position = 0;

        using var buffer = new MemoryStream();

        context.Response.Body.CopyTo(buffer);

        return buffer.ToArray();
    }

    /// <summary>
    /// A model annotated the way MessagePack requires. <c>partial</c> because the source generator
    /// writes the formatter into this type's compilation and needs access to its members.
    /// </summary>
    [MessagePackObject]
    public partial record Payload([property: Key(0)] string Name, [property: Key(1)] int Count);

    /// <summary>A model with no keys, so the formatter is written by property name.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public partial record Named(string Name, int Count);
}
