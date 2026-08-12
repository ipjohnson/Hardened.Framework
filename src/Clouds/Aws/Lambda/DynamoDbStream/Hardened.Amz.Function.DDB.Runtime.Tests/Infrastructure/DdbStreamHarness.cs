using Amazon.Lambda.DynamoDBEvents;
using Hardened.Amz.Function.DDB.Runtime.Impl;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hardened.Amz.Function.DDB.Runtime.Tests.Infrastructure;

/// <summary>
/// Runs the real <see cref="DynamoDbExecutionFilter"/> over a real <see cref="DynamoDBEvent"/> and
/// reads back the <see cref="StreamsEventResponse"/> it serialised.
///
/// <para>
/// A DynamoDB stream record never reaches the handler as a request body — the filter publishes it
/// on <see cref="CurrentDdbRecordContext"/> and the <c>[NewImage]</c> / <c>[OldImage]</c> binding
/// attributes read it back from the request scope. The harness registers one context instance in
/// that scope so the binding path under test is the production one.
/// </para>
/// </summary>
public static class DdbStreamHarness {

    public const string Insert = "INSERT";
    public const string Modify = "MODIFY";
    public const string Remove = "REMOVE";

    public static DynamoDBEvent.DynamodbStreamRecord Record(
        string eventId,
        string eventName = Modify,
        Dictionary<string, DynamoDBEvent.AttributeValue>? newImage = null,
        Dictionary<string, DynamoDBEvent.AttributeValue>? oldImage = null) {

        return new DynamoDBEvent.DynamodbStreamRecord {
            EventID = eventId,
            EventName = eventName,
            Dynamodb = new DynamoDBEvent.StreamRecord {
                NewImage = newImage,
                OldImage = oldImage
            }
        };
    }

    public static Dictionary<string, DynamoDBEvent.AttributeValue> Image(params (string Key, string Value)[] values) {
        return values.ToDictionary(v => v.Key, v => new DynamoDBEvent.AttributeValue { S = v.Value });
    }

    public static async Task<StreamsEventResponse> Run(
        IEnumerable<DynamoDBEvent.DynamodbStreamRecord> records,
        Func<IExecutionContext, CurrentDdbRecordContext, Task>? perRecord = null,
        IBatchProcessorExceptionHandler? exceptionHandler = null) {

        var recordContext = new CurrentDdbRecordContext();
        var services = new StubServiceProvider().Add(recordContext);

        using var requestBody = TestJson.ToStream(new DynamoDBEvent { Records = records.ToList() });
        using var responseBody = new MemoryStream();

        var context = TestExecutionContext.Create(requestBody, responseBody, services);

        var filter = new DynamoDbExecutionFilter(
            TestJson.Serializer,
            TestJson.Pool,
            exceptionHandler ?? new BatchProcessorExceptionHandler(),
            recordContext,
            NullLogger<DynamoDbExecutionFilter>.Instance);

        var chain = new TestExecutionChain(
            context, forked => perRecord?.Invoke(forked, recordContext) ?? Task.CompletedTask);

        await filter.Execute(chain);

        return TestJson.FromStream<StreamsEventResponse>(responseBody);
    }

    public static IReadOnlyList<string> FailedIds(StreamsEventResponse response) {
        return response.BatchItemFailures?.Select(f => f.ItemIdentifier).ToList() ?? [];
    }
}
