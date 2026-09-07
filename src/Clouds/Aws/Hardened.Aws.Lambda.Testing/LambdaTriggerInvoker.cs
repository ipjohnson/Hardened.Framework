using System.Text;
using System.Text.Json;
using Amazon.Lambda.Core;
using Hardened.Aws.Lambda.Runtime.Hosting;

namespace Hardened.Aws.Lambda.Testing;

/// <summary>
/// Turns a message and a route into the envelope AWS would have sent, and invokes.
/// </summary>
/// <remarks>
/// <para>
/// The point of the whole façade. A test says <c>SendTo.OrdersNew(order)</c> and what runs is the
/// real payload through the real loop: the adapter recognises it, the peek picks it where more than
/// one is registered, the batch filter forks it, the binder binds it. A helper that called the
/// handler directly would test almost nothing that is not already covered.
/// </para>
/// <para>
/// The envelopes are the wire shapes rather than serialized DTOs, for the reason the adapter tests
/// give: <c>eventSourceARN</c> is capitalised in a way no naming policy produces, so a fixture
/// built by round-tripping a DTO would agree with the type it came from and not with AWS.
/// </para>
/// </remarks>
public sealed class LambdaTriggerInvoker {
    private readonly LambdaInvocationHandler _handler;
    private readonly string _region;
    private readonly string _account;

    public LambdaTriggerInvoker(
        LambdaInvocationHandler handler,
        string region = "us-east-1",
        string account = "123456789012") {
        _handler = handler;
        _region = region;
        _account = account;
    }

    /// <summary>
    /// camelCase, which is what a publisher sends and what the handler's binder is set up to read.
    /// </summary>
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The delegate a generated façade is constructed with.
    /// </summary>
    /// <remarks>
    /// A <c>Func</c> of types that need no package, so the façade compiled into an application
    /// references nothing from here - only a test project does.
    /// </remarks>
    public Func<object, string, string, Task> Invoke => Send;

    /// <summary>Builds the façade for one trigger kind, wired to this invoker.</summary>
    public TFacade Facade<TFacade>() =>
        (TFacade)Activator.CreateInstance(typeof(TFacade), Invoke)!;

    private async Task Send(object messages, string scheme, string path) {
        var name = path.TrimStart('/');
        var items = messages as System.Collections.IEnumerable
                    ?? throw new ArgumentException(
                        "A façade always passes an array, so this is not reachable from generated " +
                        "code.", nameof(messages));

        var payload = scheme switch {
            "QUEUE" => Sqs(name, items),
            "TOPIC" => Sns(name, items),
            "TIMER" => Scheduled(name),
            _ => throw new NotSupportedException(
                $"No test envelope is built for the {scheme} scheme yet. Queues, topics and timers " +
                "have one; events are addressed by source and detail type and need their own shape.")
        };

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        await _handler.Invoke(input, new TestContext(name));
    }

    private string Sqs(string queue, System.Collections.IEnumerable messages) {
        var records = new List<string>();
        var index = 0;

        foreach (var message in messages) {
            var body = JsonSerializer.Serialize(message, Wire);

            records.Add($$"""
                {"messageId":"{{queue}}-{{index}}","receiptHandle":"receipt-{{index}}",
                 "body":{{JsonSerializer.Serialize(body)}},
                 "eventSource":"aws:sqs",
                 "eventSourceARN":"arn:aws:sqs:{{_region}}:{{_account}}:{{queue}}",
                 "awsRegion":"{{_region}}"}
                """);

            index++;
        }

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    private string Sns(string topic, System.Collections.IEnumerable messages) {
        var records = new List<string>();
        var index = 0;

        foreach (var message in messages) {
            var body = JsonSerializer.Serialize(message, Wire);

            var sns =
                $$"""
                  {"Type":"Notification","MessageId":"{{topic}}-{{index}}",
                   "TopicArn":"arn:aws:sns:{{_region}}:{{_account}}:{{topic}}",
                   "Message":{{JsonSerializer.Serialize(body)}}}
                  """;

            records.Add($$"""
                {"EventVersion":"1.0","EventSource":"aws:sns",
                 "EventSubscriptionArn":"arn:aws:sns:{{_region}}:{{_account}}:{{topic}}:sub-{{index}}",
                 "Sns":{{sns}}}
                """);

            index++;
        }

        return "{\"Records\":[" + string.Join(",", records) + "]}";
    }

    private string Scheduled(string rule) => $$"""
        {"version":"0","id":"{{rule}}-fired","detail-type":"Scheduled Event","source":"aws.events",
         "account":"{{_account}}","time":"2026-01-01T00:00:00Z","region":"{{_region}}",
         "resources":["arn:aws:events:{{_region}}:{{_account}}:rule/{{rule}}"],
         "detail":{} }
        """;

    /// <summary>Enough context to invoke, with a deadline the host turns into a token.</summary>
    private sealed class TestContext : ILambdaContext {
        public TestContext(string name) {
            FunctionName = name;
        }

        public string FunctionName { get; }

        public string AwsRequestId => Guid.NewGuid().ToString();
        public IClientContext ClientContext => null!;
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:" + FunctionName;
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/" + FunctionName;
        public string LogStreamName => "test";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
