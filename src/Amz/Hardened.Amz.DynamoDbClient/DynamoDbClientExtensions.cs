using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Hardened.Shared.Runtime.Utilities;

namespace Hardened.Amz.DynamoDbClient;

public static class DynamoDbClientExtensions
{
    public static Task<GetItemResponse> Get(
        this AmazonDynamoDBClient client, 
        string table,
        string primaryKey, 
        object? sortKey = null, 
        CancellationToken cancellationToken = default)
    {
        var keys = new Dictionary<string, AttributeValue>
        {
            {"PK", new AttributeValue(primaryKey)}
        };

        if (sortKey != null)
        {
            AttributeValue? value;
            
            switch (sortKey)
            {
                case bool boolValue:
                    value = new AttributeValue { BOOL = boolValue };
                    break;
                
                case decimal:
                case double:
                case int:
                case long:
                    value = new AttributeValue { N = sortKey.ToString() }; 
                    break;
                
                case DateTime dateTime:
                    value = new AttributeValue { N = dateTime.ToEpoch().ToString() };
                    break;
                
                default:
                    value = new AttributeValue(sortKey.ToString());
                    break;
            }

            keys["SK"] = value;
        }
        
        return client.GetItemAsync(
            table, 
            keys, 
            cancellationToken);
    }
}