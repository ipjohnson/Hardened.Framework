using System.Collections.Concurrent;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.DynamoDbClient;

/// <summary>
/// Builds each client on first use and keeps it for the process.
///
/// <para>
/// Caching matters: <c>AmazonDynamoDBClient</c> is thread-safe and owns a connection pool, so
/// constructing one per request is a well-known way to exhaust sockets.
/// </para>
/// </summary>
[SingletonService(As = typeof(IDynamoDbClientProvider))]
public sealed class DynamoDbClientProvider(
    IOptions<IDynamoDbOptions> options,
    IServiceProvider serviceProvider) : IDynamoDbClientProvider, IDisposable {

    private readonly ConcurrentDictionary<string, IAmazonDynamoDB> _clients = new(StringComparer.Ordinal);

    public IAmazonDynamoDB GetClient(string clientName = "") =>
        _clients.GetOrAdd(clientName, Build);

    private IAmazonDynamoDB Build(string clientName) {
        if (!string.IsNullOrEmpty(clientName)) {
            if (!options.Value.Clients.TryGetValue(clientName, out var factory)) {
                throw new InvalidOperationException(
                    $"No DynamoDB client is configured under the name '{clientName}'. " +
                    $"Configured names: {Describe(options.Value.Clients.Keys)}.");
            }

            return factory(serviceProvider);
        }

        if (options.Value.DefaultClient is { } defaultFactory) {
            return defaultFactory(serviceProvider);
        }

        return BuildFromOptions();
    }

    private IAmazonDynamoDB BuildFromOptions() {
        var (config, credentials) = DefaultClientSettings(options.Value);

        return credentials is null
            ? new AmazonDynamoDBClient(config)
            : new AmazonDynamoDBClient(credentials, config);
    }

    /// <summary>
    /// What the default client is built from, as a value rather than a client.
    ///
    /// <para>
    /// Separated because the decision here is the interesting part and a built client hides half of
    /// it: the SDK exposes a client's endpoint and region but not its credentials, and choosing
    /// credentials is exactly what the <c>ServiceUrl</c> branch does.
    /// </para>
    /// </summary>
    /// <returns>
    /// The configuration, and the credentials to build with — <c>null</c> meaning the SDK should
    /// resolve them itself from the environment and the role.
    /// </returns>
    internal static (AmazonDynamoDBConfig Config, AWSCredentials? Credentials) DefaultClientSettings(
        IDynamoDbOptions options) {
        var config = new AmazonDynamoDBConfig();
        var hasRegion = !string.IsNullOrWhiteSpace(options.Region);

        if (string.IsNullOrWhiteSpace(options.ServiceUrl)) {
            // Deployed: the SDK resolves credentials from the role, and the region for itself when
            // nothing here has said which one.
            if (hasRegion) {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
            }

            return (config, null);
        }

        config.ServiceURL = options.ServiceUrl;

        // RegionEndpoint and ServiceURL are mutually exclusive on the SDK's config — assigning
        // either clears the other — so a region given alongside an endpoint has to be carried as
        // the signing region or it is silently dropped. Which is what happened: a process with
        // both AWS_REGION and DYNAMODB_SERVICE_URL set, the ordinary local-development shape,
        // signed as us-east-1 whatever its region said.
        if (hasRegion) {
            config.AuthenticationRegion = options.Region;
        }

        // DynamoDB Local authenticates nothing, but the SDK signs every request, so credentials
        // have to exist. These values are arbitrary and meaningless.
        return (config, new BasicAWSCredentials("local", "local"));
    }

    private static string Describe(IEnumerable<string> names) {
        var listed = string.Join(", ", names.Select(n => $"'{n}'"));

        return listed.Length == 0 ? "none" : listed;
    }

    public void Dispose() {
        foreach (var client in _clients.Values) {
            client.Dispose();
        }

        _clients.Clear();
    }
}
