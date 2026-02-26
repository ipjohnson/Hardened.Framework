using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Testcontainers.DynamoDb;

namespace Hardened.Amz.DynamoDbClient.Testing;

public class LocalDynamoDbAttribute : Attribute,
    IHardenedTestDependencyRegistrationAttribute,
    IHardenedTestStartupAttribute {
    private static readonly ConcurrentDictionary<string, SharedContainer> Containers = new();
    private static readonly SemaphoreSlim InitLock = new(1, 1);

    public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceCollection serviceCollection) {
        serviceCollection.AddSingleton<IDynamoDbClientProvider>(_ =>
            new TestDynamoDbClientProvider());
    }

    private static SharedContainer GetOrCreateContainer() {
        const string key = "default";

        if (Containers.TryGetValue(key, out var existing)) {
            return existing;
        }

        InitLock.Wait();
        try {
            if (Containers.TryGetValue(key, out existing)) {
                return existing;
            }

            var container = new DynamoDbBuilder("amazon/dynamodb-local:latest")
                .Build();

            container.StartAsync().GetAwaiter().GetResult();

            var connectionString = container.GetConnectionString();
            var shared = new SharedContainer(container, connectionString);
            Containers[key] = shared;
            return shared;
        }
        finally {
            InitLock.Release();
        }
    }

    private class TestDynamoDbClientProvider : IDynamoDbClientProvider, IDisposable {
        private AmazonDynamoDBClient? _dynamoDbClient;

        public AmazonDynamoDBClient GetClient(string clientName = "") {
            if (_dynamoDbClient == null) {
                var shared = GetOrCreateContainer();
                var clientConfig = new AmazonDynamoDBConfig {
                    ServiceURL = shared.ConnectionString
                };
                var credentials = new BasicAWSCredentials("fakeKey", "secretKey");
                _dynamoDbClient = new AmazonDynamoDBClient(credentials, clientConfig);
            }

            return _dynamoDbClient;
        }

        public void Dispose() {
            try {
                _dynamoDbClient?.Dispose();
            }
            catch (Exception) { }
        }
    }

    private record SharedContainer(DynamoDbContainer Container, string ConnectionString);

    public Task Startup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceProvider serviceProvider) {
        return DdbSetup(attributeCollection, methodInfo, environment, serviceProvider);
    }

    protected virtual Task DdbSetup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IHardenedEnvironment environment,
        IServiceProvider serviceProvider) {
        return Task.CompletedTask;
    }
}
