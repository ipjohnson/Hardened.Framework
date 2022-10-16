using Amazon.DynamoDBv2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Amz.DynamoDbClient;

public interface IDynamoDbClientProvider
{
    /// <summary>
    /// Get configured dynamo db client
    /// </summary>
    /// <param name="clientName">get client by name, if string is null or empty provide default client</param>
    /// <returns></returns>
    AmazonDynamoDBClient GetClient(string clientName = "");
}
