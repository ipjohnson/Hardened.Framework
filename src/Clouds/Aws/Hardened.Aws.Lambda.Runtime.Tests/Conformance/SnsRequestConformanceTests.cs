using System.Text;
using Amazon.Lambda.SNSEvents;
using Hardened.Aws.Lambda.Runtime.Adapters;
using Hardened.Aws.Lambda.Runtime.Execution;
using Hardened.Aws.Lambda.Runtime.Tests.Infrastructure;
using Hardened.Aws.Lambda.Sns;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Testing.Conformance;

namespace Hardened.Aws.Lambda.Runtime.Tests.Conformance;

/// <summary>
/// The request a topic handler meets, held to the payload-shaped profile.
/// </summary>
/// <remarks>
/// The fork rather than the delivery, as with SQS. The body is the published message rather than
/// the notification envelope, which is the assertion that matters most here: a handler binding its
/// own type must get what the publisher sent, not what SNS wrapped it in.
/// </remarks>
public class SnsRequestConformanceTests : PayloadExecutionRequestConformanceTests {
    protected override IExecutionRequestConformanceAdapter Adapter { get; } = new SnsAdapter_();

    private sealed class SnsAdapter_ : IExecutionRequestConformanceAdapter {
        private readonly SnsAdapter _adapter = new();

        public string TransportName => "Lambda SNS";

        public IExecutionRequest CreateRequest(ConformanceRequestSpec spec) {
            var record = new SNSEvent.SNSRecord {
                EventSource = SnsAdapter.EventSourceValue,
                EventSubscriptionArn = "arn:aws:sns:us-east-1:123456789012:conformance:0b6941f8",
                Sns = new SNSEvent.SNSMessage {
                    MessageId = "conformance",
                    TopicArn = "arn:aws:sns:us-east-1:123456789012:conformance",
                    Message = spec.Body == null ? null : Encoding.UTF8.GetString(spec.Body),
                    MessageAttributes = spec.Headers.ToDictionary(
                        header => header.Key,
                        header => new SNSEvent.MessageAttribute {
                            Type = "String",
                            Value = header.Value
                        })
                }
            };

            using var payload = Payloads.Sns(record);

            var delivery = (SnsRequest)_adapter.CreateRequest(payload, TestLambdaContext.Instance);

            return delivery.ForRecord(delivery.Records[0]).Clone(method: spec.Method, path: spec.Path);
        }
    }
}
