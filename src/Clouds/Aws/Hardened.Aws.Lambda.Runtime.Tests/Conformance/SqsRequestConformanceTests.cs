using System.Text;
using Amazon.Lambda.SQSEvents;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;

namespace Hardened.Aws.Lambda.Runtime.Tests.Conformance;

/// <summary>
/// The request a queue handler actually meets, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// <para>
/// The per-record fork rather than the batch request, because the fork is what reaches a handler.
/// The batch exists to be forked: its body is the whole payload and its headers are empty, which
/// are answers about a batch rather than about a message, and holding it to a profile written for
/// one record would assert the wrong thing.
/// </para>
/// <para>
/// The spec's headers arrive as SQS message attributes, which is the only header-like channel a
/// queue has. String attributes only - a binary attribute is not a header.
/// </para>
/// </remarks>
public class SqsRequestConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new SqsAdapter_();

    private sealed class SqsAdapter_ : IExecutionRequestConformanceAdapter {
        private readonly SqsAdapter _adapter = new();

        public string TransportName => "Lambda SQS";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var record = new SQSEvent.SQSMessage {
                MessageId = "conformance",
                ReceiptHandle = "conformance-receipt",
                EventSource = SqsAdapter.EventSourceValue,
                EventSourceArn = "arn:aws:sqs:us-east-1:123456789012:conformance",
                Body = spec.Body == null ? null : Encoding.UTF8.GetString(spec.Body),
                MessageAttributes = spec.Headers.ToDictionary(
                    header => header.Key,
                    header => new SQSEvent.MessageAttribute {
                        StringValue = header.Value,
                        DataType = "String"
                    })
            };

            using var payload = Payloads.Sqs(record);

            var batch = (SqsRequest)_adapter.CreateRequest(payload, TestLambdaContext.Instance);

            // Method and path come off the event rather than the spec - QUEUE and the queue's name -
            // so the spec's are applied through Clone, the same door a filter uses. The same
            // concession InvokeRequestConformanceTests makes, and for the same reason: asking a
            // queue message to surface "GET /" is asking it to lie.
            return batch.ForRecord(batch.Records[0]).Clone(method: spec.Method, path: spec.Path);
        }
    }
}
