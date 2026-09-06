using Amazon.Lambda.SQSEvents;
using Hardened.Amz.Function.Lambda.Runtime.Filter;
using Hardened.Amz.Function.Sqs.Runtime.Impl;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.Logging.Abstractions;
using Hardened.Shared.Runtime.Metrics;

namespace Hardened.Amz.Function.Sqs.Runtime.Tests.Infrastructure;

/// <summary>
/// Runs the real <see cref="SqsBatchFilter"/> over a real <see cref="SQSEvent"/> and hands back the
/// <see cref="SQSBatchResponse"/> it serialised, read back through the same serializer the runtime
/// uses.
///
/// <para>
/// Nothing here is substituted except the chain the filter forks onto, because the mapping under
/// test is exactly "which message id ends up in the response" — a stand-in serializer or a stand-in
/// batch response would only confirm the test agrees with itself.
/// </para>
/// </summary>
public static class SqsBatchHarness {

    public static SQSEvent.SQSMessage Message(string messageId, string body) {
        return new SQSEvent.SQSMessage { MessageId = messageId, Body = body };
    }

    public static async Task<SQSBatchResponse> Run(
        IEnumerable<SQSEvent.SQSMessage> messages,
        Func<IExecutionContext, string, Task>? perMessage = null,
        IBatchProcessorExceptionHandler? exceptionHandler = null) {

        var sqsEvent = new SQSEvent { Records = messages.ToList() };

        using var requestBody = TestJson.ToStream(sqsEvent);
        using var responseBody = new MemoryStream();

        var context = TestExecutionContext.Create(requestBody, responseBody);

        var filter = new SqsBatchFilter(
            TestJson.Serializer,
            TestJson.Pool,
            exceptionHandler ?? new BatchProcessorExceptionHandler(),
            new NullMetricLoggerProvider(),
            NullLogger<SqsBatchFilter>.Instance);

        var chain = new TestExecutionChain(context, forked => {
            var body = TestExecutionContext.ReadAll(forked.Request.Body);

            return perMessage?.Invoke(forked, body) ?? Task.CompletedTask;
        });

        await filter.Execute(chain);

        return TestJson.FromStream<SQSBatchResponse>(responseBody);
    }

    public static IReadOnlyList<string> FailedIds(SQSBatchResponse response) {
        return response.BatchItemFailures.Select(f => f.ItemIdentifier).ToList();
    }
}
