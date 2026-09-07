using System.Text;
using Amazon.Lambda.Core;
using DependencyModules.Testing.Attributes;
using Hardened.Aws.Lambda.Runtime.Hosting;
using Hardened.IntegrationTests.Events.SUT;
using Hardened.Aws.Lambda.Sqs;
using Hardened.Shared.Testing.Attributes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.IntegrationTests.Events.SUT.Tests;

/// <summary>
/// The two cases a façade cannot express yet, sent as payloads.
///
/// <para>
/// <c>[Event]</c> has no façade because it is addressed by source <em>and</em> detail type, two
/// segments where every other trigger has one, so a method name cannot carry it. And a payload no
/// adapter claims has no façade by definition - it is the case where the deployment wired a source
/// the code does not serve.
/// </para>
/// </summary>
public class EnvelopeTests {

    private static Task<Stream> Invoke(IServiceProvider provider, string payload) =>
        provider.GetRequiredService<LambdaInvocationHandler>()
            .Invoke(new MemoryStream(Encoding.UTF8.GetBytes(payload)), new Context());

    /// <summary>
    /// The same envelope a schedule arrives in, told apart by its source, and bound from its detail
    /// rather than the envelope around it.
    /// </summary>
    [HardenedTest]
    public async Task ABusEventReachesItsHandlerAndBindsItsDetail(
        IServiceProvider provider, [Mock] ITriggerLog log) {
        await Invoke(provider, """
            {"version":"0","id":"7bf73129","detail-type":"OrderPlaced","source":"com.acme.orders",
             "account":"123456789012","time":"2026-09-07T12:00:00Z","region":"us-east-1",
             "resources":[],"detail":{"id":"e-1","quantity":5}}
            """);

        log.Received().Record("event:e-1");
    }

    /// <summary>
    /// A source the deployment wired that no handler asked for. Guessing would hand it to code
    /// written for another shape, so the invocation fails and names what the function serves.
    /// </summary>
    [HardenedTest]
    public async Task APayloadNoAdapterClaimsFailsTheInvocation(
        IServiceProvider provider, [Mock] ITriggerLog log) {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Invoke(provider,
                """{"Records":[{"eventSource":"aws:kinesis","kinesis":{"data":"aGk="}}]}"""));

        Assert.Contains("SqsAdapter", failure.Message);

        log.DidNotReceive().Record(Arg.Any<string>());
    }

    private sealed class Context : ILambdaContext {
        public string AwsRequestId => "integration";
        public IClientContext ClientContext => null!;
        public string FunctionName => "events-function";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-1:123456789012:function:events";
        public ILambdaLogger Logger => null!;
        public string LogGroupName => "/aws/lambda/events";
        public string LogStreamName => "stream";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromSeconds(30);
    }
}
