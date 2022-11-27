using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Hardened.Amz.DynamoDbClient.Testing.Impl;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Hardened.Amz.DynamoDbClient.Testing;

public class LocalDynamoDbAttribute : Attribute,
    IHardenedTestDependencyRegistrationAttribute,
    IHardenedTestStartupAttribute
{
    public virtual string LibPath { get; set; } = "../../../../tools/LocalDynamoDb";

    public void RegisterDependencies(AttributeCollection attributeCollection, MethodInfo methodInfo,
        IEnvironment environment,
        IServiceCollection serviceCollection)
    {
        var (dynamoService, dynamoPort) = CreateLocalDynamoDb();

        serviceCollection.AddSingleton<IDynamoDbClientProvider>(_ =>
            new TestDynamoDbClientProvider(dynamoPort, dynamoService));
    }

    private (LocalDynamoDbWrapper dynamoService, int dynamoPort) CreateLocalDynamoDb()
    {
        var random = new Random();
        Exception? exp = null;
        for (var i = 0; i < 10; i++)
        {
            try
            {
                var tempPort = random.Next(50000, 60000);

                return (new LocalDynamoDbWrapper(LibPath, null, tempPort), tempPort);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                exp = e;
            }
        }
        
        throw new Exception($"Could not create local dynamo db {exp!.Message}\r\n{exp!.StackTrace}");
    }

    private class TestDynamoDbClientProvider : IDynamoDbClientProvider, IDisposable
    {
        private AmazonDynamoDBClient? _dynamoDbClient;
        private readonly int _port;
        private readonly LocalDynamoDbWrapper _localDynamoDbWrapper;

        public TestDynamoDbClientProvider(int port, LocalDynamoDbWrapper localDynamoDbWrapper)
        {
            _port = port;
            _localDynamoDbWrapper = localDynamoDbWrapper;
        }

        public AmazonDynamoDBClient GetClient(string clientName = "")
        {
            if (_dynamoDbClient == null)
            {
                var clientConfig = new AmazonDynamoDBConfig();
                clientConfig.ServiceURL = $"http://localhost:{_port}";
                var credentials = new BasicAWSCredentials("fakeKey", "secretKey");
                _dynamoDbClient = new AmazonDynamoDBClient(credentials, clientConfig);
            }

            return _dynamoDbClient;
        }

        public void Dispose()
        {
            try
            {
                _dynamoDbClient?.Dispose();
            }
            catch (Exception)
            {

            }

            try
            {
                _localDynamoDbWrapper.Dispose();
            }
            catch (Exception)
            {

            }
        }
    }

    public Task Startup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        IEnvironment environment,
        IServiceProvider serviceProvider)
    {
        return DdbSetup(attributeCollection, methodInfo, environment, serviceProvider);
    }

    protected virtual Task DdbSetup(
        AttributeCollection attributeCollection, 
        MethodInfo methodInfo,
        IEnvironment environment,
        IServiceProvider serviceProvider)
    {
        return Task.CompletedTask;
    }
}