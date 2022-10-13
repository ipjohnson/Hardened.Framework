using Amazon.DynamoDBv2;
using Hardened.Shared.Runtime.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.DynamoDbClient;

[ConfigurationModel]
public partial class DynamoDbClientConfiguration
{
    private Func<IServiceProvider, AmazonDynamoDBConfig>? _defaultClientConfig;
    private Dictionary<string, Func<IServiceProvider, AmazonDynamoDBConfig>> _namedConfigs = new();
}
