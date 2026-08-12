using Amazon.Lambda.DynamoDBEvents;
using DynamoDbStreamApp.Impl;
using Hardened.Amz.Function.DDB.Testing;
using Hardened.Shared.Testing.Attributes;
using Xunit;

namespace DynamoDbStreamApp.Tests.Impl;

public class StreamHandlerTest {
    [HardenedTest]
    public async Task ProcessRecord(TestDynamoDbStream stream, CountingService countingService) {
        var response = await stream.ProcessUpdates(new DynamoDBEvent.DynamodbStreamRecord {
            Dynamodb = new DynamoDBEvent.StreamRecord {
                NewImage = GetNewImage(),
                OldImage = GetOldImage()
            }
        });
        
        Assert.Equal(1, countingService.Count);
    }

    private Dictionary<string,DynamoDBEvent.AttributeValue> GetOldImage() {
        return new Dictionary<string, DynamoDBEvent.AttributeValue>();
    }

    private Dictionary<string,DynamoDBEvent.AttributeValue> GetNewImage() {
        return new Dictionary<string, DynamoDBEvent.AttributeValue>();
    }
}