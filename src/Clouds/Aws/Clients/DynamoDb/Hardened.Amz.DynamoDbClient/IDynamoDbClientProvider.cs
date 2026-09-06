using Amazon.DynamoDBv2;

namespace Hardened.Amz.DynamoDbClient;

/// <summary>
/// Supplies DynamoDB clients by name.
///
/// <para>
/// A provider rather than a registered <see cref="IAmazonDynamoDB"/>, because one registration can
/// only describe one client. Reaching a second account, assuming a different role, or talking to
/// another region all need their own credentials and configuration, and a container that resolves a
/// single instance has nowhere to put them. Construction is also deferred: a factory runs when a
/// client is first asked for, not when the collection is built.
/// </para>
///
/// <para>
/// Returns the SDK's interface, so a caller can substitute one in a test without going through this
/// at all.
/// </para>
/// </summary>
public interface IDynamoDbClientProvider {
    /// <param name="clientName">
    /// Empty for the default client. A name selects one configured under that name — a second
    /// region, another account, a role with narrower permissions.
    /// </param>
    /// <exception cref="InvalidOperationException">No client is configured under that name.</exception>
    IAmazonDynamoDB GetClient(string clientName = "");
}
