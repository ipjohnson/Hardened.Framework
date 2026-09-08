using Hardened.Azure.Functions.CosmosDb;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.IntegrationTests.AzureChange.SUT;

/// <summary>
/// The entry point a change feed function is anchored on.
/// </summary>
/// <remarks>
/// <para>
/// The adapter and the batch fan-out filter arrive because a handler in this project wrote
/// <c>[Change]</c>, the way every other fixture's do. What is different is the line below it:
/// <c>[Change("orders")]</c> names a container, and a Cosmos container lives in a database the
/// neutral trigger has no slot for. That is a deployment fact rather than a property of the code,
/// so it is said once on the application, and the generator writes it into every change feed
/// function's binding - and refuses to build without it, as HRDAZ003.
/// </para>
/// <para>
/// It is the one line here that names the store. <c>ChangeTestApp</c> on DynamoDB is this file
/// without it, because a DynamoDB table stands on its own.
/// </para>
/// </remarks>
[HardenedModule]
[CosmosDbModule(Database = "orders-db")]
public partial class AzureChangeTestApp {
}
