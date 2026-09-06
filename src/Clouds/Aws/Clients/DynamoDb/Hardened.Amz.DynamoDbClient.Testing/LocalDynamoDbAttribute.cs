using System.Reflection;
using Amazon.DynamoDBv2;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.DynamoDbClient.Testing;

/// <summary>
/// Points the application's <see cref="IDynamoDbClientProvider"/> at a real DynamoDB in a container,
/// so data tests exercise the engine rather than a fake. Asserting an item shape against a mock only
/// confirms the test agrees with itself.
///
/// <para>
/// Derive from this and override <see cref="DdbSetup"/> to create the tables under test:
/// </para>
/// <code>
/// public class OrdersDatabaseAttribute : LocalDynamoDbAttribute {
///     protected override async Task DdbSetup(..., IServiceProvider services) {
///         var client = services.GetRequiredService&lt;IDynamoDbClientProvider&gt;().GetClient();
///         await client.CreateTableAsync(...);
///     }
/// }
/// </code>
///
/// <para>
/// This is the Hardened-flavoured convenience. <see cref="LocalDynamoDb"/> is the same container
/// without any of it, for a project wiring its clients some other way.
/// </para>
/// </summary>
public class LocalDynamoDbAttribute : Attribute,
    IHardenedTestDependencyRegistrationAttribute,
    IHardenedTestStartupAttribute {

    /// <summary>
    /// The container image, so a suite can pin a version rather than track <c>latest</c>:
    /// <c>[LocalDynamoDb(Image = "amazon/dynamodb-local:3.3.1")]</c>. Attributes naming different
    /// images get their own containers.
    /// </summary>
    public string Image { get; set; } = LocalDynamoDb.DefaultImage;

    public void RegisterDependencies(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection) {
        var image = Image;

        // Registered last, so it wins over whatever the application registered.
        serviceCollection.AddSingleton<IDynamoDbClientProvider>(_ => new ContainerClientProvider(image));
    }

    public Task Startup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceProvider serviceProvider) =>
        DdbSetup(attributeCollection, methodInfo, environment, serviceProvider);

    /// <summary>Create the tables under test. Runs before every test carrying this attribute.</summary>
    protected virtual Task DdbSetup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceProvider serviceProvider) => Task.CompletedTask;

    /// <summary>
    /// Every name resolves to the same container. A test asserting behaviour across two accounts is
    /// asserting something DynamoDB Local cannot represent, so pretending otherwise would only make
    /// the failure harder to read.
    /// </summary>
    private sealed class ContainerClientProvider(string image) : IDynamoDbClientProvider, IDisposable {
        private readonly Lazy<IAmazonDynamoDB> _client = new(() => LocalDynamoDb.CreateClient(image));

        public IAmazonDynamoDB GetClient(string clientName = "") => _client.Value;

        public void Dispose() {
            if (_client.IsValueCreated) {
                _client.Value.Dispose();
            }
        }
    }
}
