using Amazon.CloudWatch.Model;
using Amazon.DynamoDBv2.Model;
using Hardened.Amz.Canaries.Runtime.Configuration;
using Hardened.Amz.Canaries.Runtime.Models.Flight;
using Hardened.Amz.DynamoDbClient;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.Options;

namespace Hardened.Amz.Canaries.Runtime.DynamoDb;

public interface ICanaryDynamoWriter
{
    Task WriteInitialStateDocument(IReadOnlyDictionary<string, CanaryDefinition> readOnlyDictionary);

    Task<bool> WriteCanaryStart(string canaryName, CanaryDefinition canaryDefinition, string invokeId);

    Task WriteCanaryResult(
        string canaryName,
        CanaryDefinition canaryDefinition,
        string invokeId,
        bool result,
        TimeSpan flightTime);
}

[Expose]
[Singleton]
public class CanaryDynamoWriter : ICanaryDynamoWriter
{
    private readonly IDynamoDbClientProvider _clientProvider;
    private readonly ICanaryConfigurationModel _canaryConfiguration;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ICanaryDynamoReader _canaryDynamoReader;

    public CanaryDynamoWriter(
        IDynamoDbClientProvider clientProvider,
        IOptions<ICanaryConfigurationModel> canaryConfiguration,
        IJsonSerializer jsonSerializer,
        ICanaryDynamoReader canaryDynamoReader)
    {
        _clientProvider = clientProvider;
        _canaryConfiguration = canaryConfiguration.Value;
        _jsonSerializer = jsonSerializer;
        _canaryDynamoReader = canaryDynamoReader;
    }

    public async Task WriteInitialStateDocument(IReadOnlyDictionary<string, CanaryDefinition> canaryDefinitions)
    {
        var canaryDefinitionsValues = new Dictionary<string, AttributeValue>();

        foreach (var pair in canaryDefinitions)
        {
            canaryDefinitionsValues[pair.Key] =
                new AttributeValue(_jsonSerializer.Serialize(pair.Value));
        }

        await _clientProvider.GetClient().PutItemAsync(
            _canaryConfiguration.DynamoDataTable,
            new Dictionary<string, AttributeValue>
            {
                { "PK", new AttributeValue(DynamoDbConstants.CanaryDefinitionsDocument) },
                { DynamoDbConstants.VersionId, new AttributeValue { N = 0.ToString() } },
                { DynamoDbConstants.CanaryDefinitions, new AttributeValue { M = canaryDefinitionsValues } },
                { DynamoDbConstants.CanaryHistory, new AttributeValue { IsMSet = true } }
            }
        );
    }

    public async Task<bool> WriteCanaryStart(
        string canaryName,
        CanaryDefinition canaryDefinition,
        string invokeId)
    {
        for (var i = 0; i < 3; i++)
        {
            TimeSpan? estimatedFlightTime = null;
            var history = await _canaryDynamoReader.GetCanaryHistory(canaryName);
            var historyList = new List<CanaryFlightInfo>();

            if (history != null)
            {
                historyList.AddRange(history.RecentFlights);

                if (historyList.Any(f => f.FlightNumber == invokeId))
                {
                    return false;
                }

                estimatedFlightTime = GetEstimatedFlightTime(historyList);
            }

            historyList.Insert(0, new CanaryFlightInfo(
                invokeId,
                FlightStatus.Inflight,
                DateTime.Now,
                null,
                estimatedFlightTime
            ));

            if (historyList.Count > 5)
            {
                historyList.RemoveAt(5);
            }

            if (await WriteHistoryList(canaryName, canaryDefinition, historyList, history?.VersionId ?? 0))
            {
                return true;
            }
        }

        return false;
    }

    private static TimeSpan? GetEstimatedFlightTime(List<CanaryFlightInfo> historyList)
    {
        var count = 0;

        double totalMilliseconds = historyList.Aggregate(0.0, (value, info) =>
        {
            if (info.FlightTime.HasValue)
            {
                count++;
                return value + info.FlightTime.Value.TotalMinutes;
            }

            return value;
        });

        if (count > 0)
        {
            return new TimeSpan(0, 0, 0, 0, (int)(totalMilliseconds / count));
        }

        return null;
    }

    public async Task WriteCanaryResult(string canaryName, CanaryDefinition canaryDefinition, string invokeId,
        bool result, TimeSpan flightTime)
    {
        for (var i = 0; i < 3; i++)
        {
            var history = await _canaryDynamoReader.GetCanaryHistory(canaryName);

            var instance = history!.RecentFlights.First(f => f.FlightNumber == invokeId);

            var newInstance = instance with
            {
                FlightStatus = result ? FlightStatus.Passed : FlightStatus.Failed, FlightTime = flightTime
            };

            var newList = new List<CanaryFlightInfo>(history.RecentFlights);

            newList.Remove(instance);

            newList.Add(newInstance);

            if (await WriteHistoryList(canaryName, canaryDefinition, newList, history.VersionId))
            {
                return;
            }
        }
    }

    private async Task<bool> WriteHistoryList(string canaryId, CanaryDefinition canaryDefinition,
        List<CanaryFlightInfo> historyList, long historyVersionId)
    {
        historyList.Sort((x, y) =>
            Comparer<DateTime>.Default.Compare(x.FlightTakeOff, y.FlightTakeOff));

        historyList.Reverse();

        var serializeList = _jsonSerializer.Serialize(new CanaryRecentFlightHistory(historyList));

        try
        {
            var expressionAttributes = new Dictionary<string, AttributeValue>
            {
                { ":inc", new AttributeValue { N = 1.ToString() } },
                { ":listValue", new AttributeValue(serializeList) }
            };

            var concurrentExpression = "attribute_not_exists(#versionId)";

            if (historyVersionId > 0)
            {
                concurrentExpression = 
                "#versionId = :versionNumber";
                
                expressionAttributes.Add(":versionNumber", new AttributeValue{ N = historyVersionId.ToString()});
            }
            
            await _clientProvider.GetClient().UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = _canaryConfiguration.DynamoDataTable,
                    Key = new Dictionary<string, AttributeValue> { { "PK", new AttributeValue("CF/" + canaryId) } },
                    UpdateExpression = "ADD versionId :inc SET #history=:listValue",
                    ExpressionAttributeNames =
                        new Dictionary<string, string>
                        {
                            { "#history", DynamoDbConstants.CanaryHistory },
                            { "#versionId", DynamoDbConstants.VersionId }
                        },
                    ExpressionAttributeValues = expressionAttributes,
                    ConditionExpression = concurrentExpression,
                }
            );

            return true;
        }
        catch (ConcurrentModificationException exp)
        {
            return false;
        }
    }
}