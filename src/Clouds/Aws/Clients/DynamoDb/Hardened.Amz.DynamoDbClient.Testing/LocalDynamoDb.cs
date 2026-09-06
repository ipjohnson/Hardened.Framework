using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Testcontainers.DynamoDb;

namespace Hardened.Amz.DynamoDbClient.Testing;

/// <summary>
/// DynamoDB Local in a container, shared by every test in the process that asks for the same image.
///
/// <para>
/// Usable on its own, without any of the rest of this package — take <see cref="Endpoint"/> and wire
/// a client however you already do, or call <see cref="CreateClient"/> for one that is already
/// pointed at it. Nothing here knows what a table or a key looks like.
/// </para>
///
/// <para>
/// One container per image rather than per test: starting one costs seconds, and tests that need
/// isolation from each other should be using distinct keys rather than distinct databases. Requires
/// a Docker daemon.
/// </para>
///
/// <para>
/// The image is an argument rather than a setting. It used to be a mutable static, which carried an
/// implicit "set this before anything touches Endpoint" contract — set it late and it was silently
/// ignored, and two tests wanting different versions could not both get what they asked for. Asking
/// per call means the answer cannot depend on what ran first.
/// </para>
/// </summary>
public static class LocalDynamoDb {
    /// <summary>What <see cref="Endpoint"/> starts when no image is named.</summary>
    public const string DefaultImage = "amazon/dynamodb-local:latest";

    private static readonly ConcurrentDictionary<string, Lazy<DynamoDbContainer>> Containers =
        new(StringComparer.Ordinal);

    /// <summary>Starts <see cref="DefaultImage"/> on first call and returns its endpoint.</summary>
    public static string Endpoint => EndpointFor(DefaultImage);

    /// <summary>
    /// Starts <paramref name="image"/> on first call and returns its endpoint. Repeated calls for
    /// the same image return the same container.
    /// </summary>
    /// <param name="image">
    /// A full Docker image name including the tag, so a suite can pin a version rather than track
    /// <c>latest</c>.
    /// </param>
    public static string EndpointFor(string image) => ContainerFor(image).GetConnectionString();

    /// <summary>
    /// A client pointed at the container. DynamoDB Local authenticates nothing, but the SDK signs
    /// every request, so credentials have to exist; these are arbitrary and meaningless.
    /// </summary>
    public static IAmazonDynamoDB CreateClient(string image = DefaultImage) =>
        new AmazonDynamoDBClient(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = EndpointFor(image) });

    /// <summary>
    /// Stops every container started here, and forgets them — a later call starts a fresh one.
    ///
    /// <para>
    /// Testcontainers' Ryuk reaps containers when the run ends regardless, so this is not needed for
    /// correctness. It is here so that a suite can end its containers at a point it chooses rather
    /// than leaving the lifetime to a sidecar, which makes the lifetime legible and reclaims the
    /// memory before the process finishes.
    /// </para>
    /// </summary>
    public static void StopAll() {
        foreach (var container in Containers.Values) {
            // A container whose Lazy has not run has nothing to stop. One being started right now
            // is a suite tearing down while it is still working, which is its own problem.
            if (container.IsValueCreated) {
                container.Value.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        Containers.Clear();
    }

    private static DynamoDbContainer ContainerFor(string image) =>
        Containers.GetOrAdd(image,
            key => new Lazy<DynamoDbContainer>(() => Start(key), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;

    private static DynamoDbContainer Start(string image) {
        var container = new DynamoDbBuilder(image).Build();

        // Blocking is deliberate — callers are synchronous DI registration paths, and this happens
        // once per image per process.
        container.StartAsync().GetAwaiter().GetResult();

        return container;
    }
}
