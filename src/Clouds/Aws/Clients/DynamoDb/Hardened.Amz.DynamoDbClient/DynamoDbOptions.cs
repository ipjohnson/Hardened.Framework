using Amazon.DynamoDBv2;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.DynamoDbClient;

/// <summary>
/// How clients are built.
///
/// <para>
/// The default client is described by two environment variables, because that covers the common
/// case and needs no code. Anything else — different credentials, an assumed role, a second region,
/// a custom retry policy — is a factory registered under a name, which receives the service provider
/// so it can resolve whatever it needs to build a client.
/// </para>
/// </summary>
[ConfigurationModel]
public partial class DynamoDbOptions {

    /// <summary>
    /// Overrides the default client's endpoint. This is what points a process at DynamoDB Local, and
    /// it should never be set in a deployed environment.
    /// </summary>
    [FromEnvironmentVariable("DYNAMODB_SERVICE_URL")]
    private string _serviceUrl = "";

    /// <summary>Left to the SDK's own resolution when empty, which is the deployed case.</summary>
    [FromEnvironmentVariable("AWS_REGION")]
    private string _region = "";

    /// <summary>
    /// Named clients. The key is what <c>GetClient(name)</c> takes; the factory decides everything
    /// about the client, credentials included.
    /// <code>
    /// config.Amend((DynamoDbOptions o) => o.Clients["audit"] = provider =>
    ///     new AmazonDynamoDBClient(assumedRoleCredentials, new AmazonDynamoDBConfig { ... }));
    /// </code>
    /// </summary>
    private Dictionary<string, Func<IServiceProvider, IAmazonDynamoDB>> _clients = new();

    /// <summary>
    /// Replaces how the default client is built. Set this and <see cref="ServiceUrl"/> and
    /// <see cref="Region"/> are ignored — a caller supplying a factory has said everything.
    /// </summary>
    private Func<IServiceProvider, IAmazonDynamoDB>? _defaultClient;
}
