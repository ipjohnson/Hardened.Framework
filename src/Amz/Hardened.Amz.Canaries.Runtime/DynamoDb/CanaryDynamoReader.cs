using Amazon.DynamoDBv2.Model;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.DynamoDbClient;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Canaries.Runtime.DynamoDb;

public interface ICanaryDynamoReader
{
    Task<CurrentCanaryState?> GetCanaryState();

    Task<CanaryRecentFlightHistory?> GetCanaryHistory(string canaryId);
}

[Expose]
[Singleton]
public class CanaryDynamoReader : ICanaryDynamoReader
{
    private readonly IDynamoDbClientProvider _clientProvider;
    private readonly ICanaryConfigurationModel _canaryConfiguration;
    private readonly IJsonSerializer _jsonSerializer;

    public CanaryDynamoReader(
        IDynamoDbClientProvider clientProvider, IOptions<ICanaryConfigurationModel> canaryModel,
        IJsonSerializer jsonSerializer)
    {
        _clientProvider = clientProvider;
        _jsonSerializer = jsonSerializer;
        _canaryConfiguration = canaryModel.Value;
    }

    public async Task<CurrentCanaryState?> GetCanaryState()
    {
        try
        {
            var result =
                await _clientProvider.GetClient().Get(
                    _canaryConfiguration.DynamoDataTable,
                    DynamoDbConstants.CanaryDefinitionsDocument
                );

            if (result.Item.Count == 0)
            {
                return null;
            }

            return ProcessCanaryStateResponse(result);
        }
        catch (ResourceNotFoundException exp)
        {
            return null;
        }
    }

    public async Task<CanaryRecentFlightHistory?> GetCanaryHistory(string canaryId)
    {
        var result = await _clientProvider.GetClient().Get(
            _canaryConfiguration.DynamoDataTable,
            "CF/" + canaryId
        );

        if (result.Item.TryGetValue(DynamoDbConstants.VersionId, out var versionId) &&
            result.Item.TryGetValue(DynamoDbConstants.CanaryHistory, out var historyValue))
        {
            var history = _jsonSerializer.Deserialize<CanaryRecentFlightHistory>(
                historyValue.S
            );

            history.VersionId = long.Parse(versionId.N);

            return history;
        }

        return null;
    }

    private CurrentCanaryState ProcessCanaryStateResponse(GetItemResponse result)
    {
        long version = 0;
        var disabledList = new List<string>();
        var canaryDefinitions = new Dictionary<string, CanaryDefinition>();
        var canaryHistory = new Dictionary<string, CanaryRecentFlightHistory>();

        foreach (var pair in result.Item)
        {
            switch (pair.Key)
            {
                case DynamoDbConstants.VersionId:
                    version = long.Parse(pair.Value.N);
                    break;
                case DynamoDbConstants.DisabledList:
                    ProcessDisabledList(pair.Value, disabledList);
                    break;
                case DynamoDbConstants.CanaryDefinitions:
                    ProcessCanaryDefinitions(pair.Value, canaryDefinitions);
                    break;
            }
        }

        return new CurrentCanaryState(version, disabledList, canaryDefinitions);
    }

    private void ProcessCanaryHistory(AttributeValue pairValue,
        Dictionary<string, CanaryRecentFlightHistory> canaryHistory)
    {
        if (pairValue.IsMSet)
        {
            foreach (var pair in pairValue.M)
            {
                canaryHistory[pair.Key] = _jsonSerializer.Deserialize<CanaryRecentFlightHistory>(
                    pair.Value.S
                );
            }
        }
    }

    private void ProcessCanaryDefinitions(AttributeValue pairValue,
        Dictionary<string, CanaryDefinition> canaryDefinitions)
    {
        if (pairValue.IsMSet)
        {
            foreach (var pair in pairValue.M)
            {
                canaryDefinitions[pair.Key] = _jsonSerializer.Deserialize<CanaryDefinition>(
                    pair.Value.S
                );
            }
        }
    }

    private void ProcessDisabledList(AttributeValue pairValue, List<string> disabledList)
    {
        if (pairValue.IsLSet)
        {
            foreach (var value in pairValue.L)
            {
                disabledList.Add(value.S);
            }
        }
    }
}