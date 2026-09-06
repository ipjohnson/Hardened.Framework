using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.DDB.Runtime.Attributes;
using Hardened.Requests.Abstract.Attributes;

namespace DynamoDbStreamApp.Impl;

public class StreamHandler {
    private readonly CountingService _countingService;

    public StreamHandler(CountingService countingService) {
        _countingService = countingService;
    }

    [HardenedFunction]
    public async Task ProcessRecord(
        [OldImage] Dictionary<string, DynamoDBEvent.AttributeValue> oldImage,
        [NewImage] Dictionary<string, DynamoDBEvent.AttributeValue> newImage) {
        _countingService.Count++;
    }
}