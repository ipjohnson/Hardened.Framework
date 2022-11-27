using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.DynamoDb;
using Hardened.Amz.DynamoDbClient;
using Hardened.Amz.DynamoDbClient.Testing;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;

namespace Hardened.Amz.Canaries.Runtime.Tests;

public class CanaryDatabaseAttribute : LocalDynamoDbAttribute
{
    public override string LibPath { get; set; } = "../../../../../../tools/";
    
    protected override async Task DdbSetup(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo, 
        IEnvironment environment,
        IServiceProvider serviceProvider)
    {
        var ddbClient = serviceProvider.GetRequiredService<IDynamoDbClientProvider>().GetClient();
        var dataTableName = 
            serviceProvider.GetRequiredService<IOptions<ICanaryConfigurationModel>>().Value.DynamoDataTable;
        
        await ddbClient.CreateTableAsync(new CreateTableRequest(
            dataTableName,
            new List<KeySchemaElement>
            {
                new ("PK", KeyType.HASH)
            },
            new List<AttributeDefinition>
            {
                new ("PK", ScalarAttributeType.S)
            },
            new ProvisionedThroughput(5,5)
            ));
    }
}