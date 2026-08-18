using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Serializers.Newtonsoft.Impl;
using Hardened.Requests.Testing;
using Hardened.Shared.Runtime.Collections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using NSubstitute;

namespace Hardened.Requests.Serializers.Newtonsoft.Tests.Support;

/// <summary>
/// Real request, real response, real pool.
/// </summary>
/// <remarks>
/// The pool is the production <see cref="MemoryStreamPool"/> rather than a substitute, and
/// deliberately. Both serializers borrow from it, and the defect this suite was written against was
/// a reservation being taken three times and read once — which a substitute handing back a fresh
/// stream every call would have hidden completely.
/// </remarks>
public static class Pipeline {

    public static MemoryStreamPool Pool() => new();

    /// <summary>
    /// The production pool, counting how many reservations were taken and how many came back.
    /// </summary>
    /// <remarks>
    /// A leaked reservation is invisible through <see cref="IItemPool{T}"/> alone — the pool simply
    /// makes another stream, so the next borrower cannot tell. Counting is the only way to assert
    /// it, and it needs asserting: the deserializer took three reservations per request and
    /// returned one until 2026-08-18.
    /// </remarks>
    public sealed class CountingPool : IMemoryStreamPool {
        private readonly MemoryStreamPool _inner = new();

        public int Taken { get; private set; }

        public int Returned { get; private set; }

        public IPoolItemReservation<MemoryStream> Get() {
            Taken++;

            return new Reservation(this, _inner.Get());
        }

        private sealed class Reservation : IPoolItemReservation<MemoryStream> {
            private readonly CountingPool _pool;
            private readonly IPoolItemReservation<MemoryStream> _inner;

            public Reservation(CountingPool pool, IPoolItemReservation<MemoryStream> inner) {
                _pool = pool;
                _inner = inner;
            }

            public MemoryStream Item => _inner.Item;

            public void Dispose() {
                _pool.Returned++;

                _inner.Dispose();
            }
        }
    }

    public static ISharedSerializer Serializer(JsonSerializer? serializer = null) {
        var shared = Substitute.For<ISharedSerializer>();

        shared.Serializer.Returns(serializer ?? JsonSerializer.CreateDefault());

        return shared;
    }

    public static NewtonsoftDeserializer Deserializer(
        IMemoryStreamPool pool, JsonSerializer? serializer = null) =>
        new(pool, Serializer(serializer), NullLogger<NewtonsoftDeserializer>.Instance);

    public static NewtonsoftSerializer ResponseSerializer(
        IMemoryStreamPool pool, JsonSerializer? serializer = null) =>
        new(Serializer(serializer), pool);

    /// <summary>A context whose request body is <paramref name="body"/>.</summary>
    public static IExecutionContext Context(
        string? body = null, string? contentType = KnownContentType.Json) {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var request = new TestExecutionRequest(
            "POST", "/", KnownContentType.Json,
            new SimpleQueryStringCollection(new Dictionary<string, string>())) {
            Body = body is null ? Stream.Null : new MemoryStream(Encoding.UTF8.GetBytes(body))
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

    /// <summary>Everything written to the response body, as text.</summary>
    public static string BodyOf(IExecutionContext context) {
        context.Response.Body.Position = 0;

        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);

        return reader.ReadToEnd();
    }

    public static IOptions<INewtonsoftSerializerConfiguration> Configuration(
        Func<IServiceProvider, JsonSerializer>? provider = null) {
        var configuration = new NewtonsoftSerializerConfiguration();

        if (provider != null) {
            configuration.SerializerProvider = provider;
        }

        return Options.Create<INewtonsoftSerializerConfiguration>(configuration);
    }

    public record Payload(string Name, int Count);
}
