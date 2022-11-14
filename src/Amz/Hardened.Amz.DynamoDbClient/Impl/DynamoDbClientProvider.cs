using Amazon.DynamoDBv2;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Amz.DynamoDbClient.Impl;

[Expose]
[Singleton]
public class DynamoDbClientProvider : IDynamoDbClientProvider
{
    private AmazonDynamoDBClient? _defaultClient;
    private readonly ConcurrentDictionary<string, AmazonDynamoDBClient> _namedClients = new();
    private readonly IDynamoDbClientConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public DynamoDbClientProvider(IOptions<IDynamoDbClientConfiguration> clientOptions, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _configuration = clientOptions.Value;
    }
    public AmazonDynamoDBClient GetClient(string clientName = "")
    {
        if (string.IsNullOrEmpty(clientName))
        {
            return DefaultClient();
        }

        return GetNamedClient(clientName);
    }

    private AmazonDynamoDBClient DefaultClient()
    {
        return _defaultClient ??= CreateDefaultClient();
    }

    private AmazonDynamoDBClient CreateDefaultClient()
    {
        if (_configuration.DefaultClientConfig != null)
        {
            return new AmazonDynamoDBClient(_configuration.DefaultClientConfig(_serviceProvider));
        }

        return new AmazonDynamoDBClient();
    }

    private AmazonDynamoDBClient GetNamedClient(string clientName)
    {
        return _namedClients.GetOrAdd(clientName, s =>
        {
            if (_configuration.NamedConfigs.TryGetValue(s, out var configFunc))
            {
                return new AmazonDynamoDBClient( configFunc(_serviceProvider));
            }

            throw new Exception("DynamoDb client not configured " + clientName);
        });

    }
}
